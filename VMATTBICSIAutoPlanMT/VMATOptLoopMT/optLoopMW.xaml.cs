﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using Microsoft.Win32;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using VMATTBICSIOptLoopMT.helpers;
using VMATTBICSIOptLoopMT.baseClasses;
using VMATTBICSIAutoplanningHelpers.helpers;
using VMATTBICSIAutoplanningHelpers.TemplateClasses;
using VMATTBICSIOptLoopMT.VMAT_CSI;
using System.Collections.ObjectModel;
using System.Reflection;

namespace VMATTBICSIOptLoopMT
{
    public partial class OptLoopMW : Window
    {
        //configuration file
        string configFile = "";
        //point this to the directory holding the documentation files
        string documentationPath = @"\\enterprise.stanfordmed.org\depts\RadiationTherapy\Public\Users\ESimiele\Research\VMAT_TBI\documentation\";
        //default number of optimizations to perform
        string defautlNumOpt = "3";
        //default plan normalization (i.e., PTV100% = 90%) 
        string defaultPlanNorm = "90";
        //run coverage check
        bool runCoverageCheckOption = false;
        //run additional optimization option
        bool runAdditionalOptOption = true;
        //copy and save each optimized plan
        bool copyAndSaveOption = false;
        //is demo
        bool demo = false;
        //log file directory
        string logFilePath = @"\\enterprise.stanfordmed.org\depts\RadiationTherapy\Public\Users\ESimiele\Research\VMAT_TBI\log_files";
        //decision threshold
        double threshold = 0.15;
        //lower dose limit
        double lowDoseLimit = 0.1;

        //structure, constraint type, relative dose, relative volume (unless otherwise specified)
        //note, if the constraint type is "mean", the relative volume value is ignored
        public List<Tuple<string, string, double, double, DoseValuePresentation>> planObj = new List<Tuple<string, string, double, double, DoseValuePresentation>> { };

        //ID, lower dose level, upper dose level, volume (%), priority, list of criteria that must be met to add the requested cooler/heater structures
        public List<Tuple<string, double, double, double, int, List<Tuple<string, double, string, double>>>> requestedTSstructures = new List<Tuple<string, double, double, double, int, List<Tuple<string, double, string, double>>>>{ };

        //structure id(or can put '<plan>' to get the plan dose value), metric requested(Dmax, Dmin, D<vol %>, V<dose %>), return value representation(dose or volume as absolute or relative)
        public List<Tuple<string, string, double, string>> planDoseInfo = new List<Tuple<string, string, double, string>> { };

        VMS.TPS.Common.Model.API.Application app = VMS.TPS.Common.Model.API.Application.CreateApplication();
        ExternalPlanSetup plan;
        StructureSet selectedSS;
        Patient pi = null;
        bool runCoverageCheck = false;
        bool runOneMoreOpt = false;
        bool copyAndSavePlanItr = false;
        bool useFlash = false;
        //ATTENTION! THE FOLLOWING LINE HAS TO BE FORMATTED THIS WAY, OTHERWISE THE DATA BINDING WILL NOT WORK!
        public ObservableCollection<CSIAutoPlanTemplate> PlanTemplates { get; set; }

        public OptLoopMW(string[] args)
        {
            InitializeComponent();
            PlanTemplates = new ObservableCollection<CSIAutoPlanTemplate>() { new CSIAutoPlanTemplate("--select--") };
            DataContext = this;
            string patmrn = "";
            string configurationFile = "";
            for (int i = 0; i < args.Length; i++)
            {
                if (i == 0) patmrn = args[i];
                if (i == 1) configurationFile = args[i];
            }
            //if (args.Length == 0) configurationFile = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\configuration\\VMAT_TBI_config.ini";
            if (args.Length == 0) configurationFile = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\configuration\\VMAT_CSI_config.ini";

            if (File.Exists(configurationFile)) 
            { 
                if (!loadConfigurationSettings(configurationFile)) DisplayConfigurationParameters(); 
            }
            else MessageBox.Show("No configuration file found! Loading default settings!");
            if (args.Length > 0) 
            { 
                MRN.Text = patmrn;
                OpenPatient(patmrn);
                OpenPatient_Click(new object(), new RoutedEventArgs()); 
            }
        }

        #region help and info buttons
        private void Help_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(documentationPath + "VMAT_TBI_guide.pdf");
        }

        private void QuickStart_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(documentationPath + "TBI_executable_quickStart_guide.pdf");
        }

        private void targetNormInfo_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("This is used to set the plan normalization. What percentage of the PTV volume should recieve the prescription dose?");
        }
        #endregion

        #region button events
        private void OpenPatient_Click(object sender, RoutedEventArgs e)
        {
            //open the patient with the user-entered MRN number
            clearEverything();
            OpenPatient(MRN.Text);
        }

        private void OpenPatient(string pat_mrn)
        {
            try
            {
                app.ClosePatient();
                pi = app.OpenPatientById(pat_mrn);
                //grab instances of the course and VMAT tbi plans that were created using the binary plug in script. This is explicitly here to let the user know if there is a problem with the course OR plan
                //Course c = pi.Courses.FirstOrDefault(x => x.Id.ToLower() == "vmat tbi");
                (plan, selectedSS) = GetStructureSetAndPlans();
                if (plan == null)
                {
                    MessageBox.Show("No plan named _VMAT TBI!");
                    return;
                }
                //ensure the correct plan target is selected and all requested objectives have a matching structure that exists in the structure set (needs to be done after structure set has been assinged)
                PopulateOptimizationTab(optimizationParamSP);

                //populate the prescription text boxes with the prescription stored in the VMAT TBI plan
                populateRx();
                //set the default parameters for the optimization loop
                runCoverageCk.IsChecked = runCoverageCheckOption;
                numOptLoops.Text = defautlNumOpt;
                runAdditionalOpt.IsChecked = runAdditionalOptOption;
                copyAndSave.IsChecked = copyAndSaveOption;
                targetNormTB.Text = defaultPlanNorm;
                planObjectiveHeader.Background = System.Windows.Media.Brushes.PaleVioletRed;
            }
            catch
            {
                MessageBox.Show("No such patient exists!");
            }
        }

        private void getOptFromPlan_Click(object sender, RoutedEventArgs e)
        {
            if (pi != null && plan != null) PopulateOptimizationTab(optimizationParamSP);
        }

        private void AddItem_Click(object sender, RoutedEventArgs e)
        {
            //add a blank contraint to the list
            if (plan != null)
            {
                (StackPanel, ScrollViewer) SPAndSV = GetSPAndSV(sender as Button);
                if(SPAndSV.Item1.Name.ToLower().Contains("optimization"))
                {
                    List<Tuple<string, string, double, double, int>> tmp = new List<Tuple<string, string, double, double, int>> { Tuple.Create("--select--", "--select--", 0.0, 0.0, 0) };
                    List<List<Tuple<string, string, double, double, int>>> tmpList = new List<List<Tuple<string, string, double, double, int>>> { };
                    if (SPAndSV.Item1.Children.Count > 0)
                    {
                        OptimizationSetupUIHelper helper = new OptimizationSetupUIHelper();
                        List<Tuple<string, List<Tuple<string, string, double, double, int>>>> optParametersListList = helper.parseOptConstraints(SPAndSV.Item1, false);
                        foreach (Tuple<string, List<Tuple<string, string, double, double, int>>> itr in optParametersListList)
                        {
                            if (itr.Item1 == plan.Id)
                            {
                                tmp = new List<Tuple<string, string, double, double, int>>(itr.Item2);
                                tmp.Add(Tuple.Create("--select--", "--select--", 0.0, 0.0, 0));
                                tmpList.Add(tmp);
                            }
                            else tmpList.Add(itr.Item2);
                        }
                    }
                    else
                    {
                        tmpList.Add(tmp);
                    }
                    ClearAllItemsFromUIList(SPAndSV.Item1);
                    foreach (List<Tuple<string, string, double, double, int>> itr in tmpList) AddListItemsToUI(itr, plan.Id, SPAndSV.Item1);
                }
                else
                {
                    List<Tuple<string, string, double, double, DoseValuePresentation>> tmp = new List<Tuple<string, string, double, double, DoseValuePresentation>> 
                    { 
                        Tuple.Create("--select--", "--select--", 0.0, 0.0, DoseValuePresentation.Relative) 
                    };
                    AddListItemsToUI(tmp, plan.Id, SPAndSV.Item1); 
                    planObjectiveHeader.Background = System.Windows.Media.Brushes.ForestGreen;
                    optimizationSetupHeader.Background = System.Windows.Media.Brushes.PaleVioletRed;
                }
                SPAndSV.Item2.ScrollToBottom();
            }
        }

        private void ClearAllItems_Click(object sender, RoutedEventArgs e) 
        {
            ClearAllItemsFromUIList(GetSPAndSV(sender as Button).Item1);
        }

        private void ClearItem_Click(object sender, EventArgs e)
        {
            StackPanel theSP = GetSPAndSV(sender as Button).Item1;
            if (new GeneralUIhelper().clearRow(sender, theSP)) ClearAllItemsFromUIList(theSP);
        }
        #endregion

        #region UI manipulation
        private void Templates_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (pi == null) return;
            CSIAutoPlanTemplate selectedTemplate = templateList.SelectedItem as CSIAutoPlanTemplate;
            if (selectedTemplate == null) return;
            if (selectedTemplate.templateName != "--select--")
            {
                ClearAllItemsFromUIList(planObjectiveParamSP);
                planObj = new List<Tuple<string, string, double, double, DoseValuePresentation>>(ConstructPlanObjectives(selectedTemplate.planObj));
                PopulatePlanObjectivesTab(planObjectiveParamSP);
                planDoseInfo = new List<Tuple<string, string, double, string>>(selectedTemplate.planDoseInfo);
                requestedTSstructures = new List<Tuple<string, double, double, double, int, List<Tuple<string, double, string, double>>>>(selectedTemplate.requestedTSstructures);
                if (selectedTemplate.planObj.Any())
                {
                    planObjectiveHeader.Background = System.Windows.Media.Brushes.ForestGreen;
                    optimizationSetupHeader.Background = System.Windows.Media.Brushes.PaleVioletRed;
                }
            }
            else
            {
                templateList.UnselectAll();
                planObj = new List<Tuple<string, string, double, double, DoseValuePresentation>>();
                ClearAllItemsFromUIList(planObjectiveParamSP);
                planDoseInfo = new List<Tuple<string, string, double, string>>();
                requestedTSstructures = new List<Tuple<string, double, double, double, int, List<Tuple<string, double, string, double>>>>();
                planObjectiveHeader.Background = System.Windows.Media.Brushes.PaleVioletRed;
                optimizationSetupHeader.Background = System.Windows.Media.Brushes.DarkGray;
            }
        }

        private (StackPanel, ScrollViewer) GetSPAndSV(Button theBTN)
        {
            StackPanel theSP;
            ScrollViewer theScroller;
            if (theBTN.Name.ToLower().Contains("optimization"))
            {
                theSP = optimizationParamSP;
                theScroller = optimizationParamScroller;
            }
            else
            {
                theSP = planObjectiveParamSP;
                theScroller = planObjectiveParamScroller;
            }
            return (theSP, theScroller);
        }

        private (ExternalPlanSetup, StructureSet) GetStructureSetAndPlans()
        {
            //grab an instance of the VMAT TBI plan. Return null if it isn't found
            if (pi == null) return (null, null);
            //Course c = pi.Courses.FirstOrDefault(x => x.Id.ToLower() == "vmat tbi");
            Course c = pi.Courses.FirstOrDefault(x => x.Id.ToLower() == "vmat csi");
            if (c == null) return (null, null);

            ExternalPlanSetup thePlan = c.ExternalPlanSetups.FirstOrDefault(x => x.Id.ToLower() == "csi-init");
            return (thePlan, thePlan.StructureSet);
        }

        private void clearEverything()
        {
            //clear all existing content from the main window
            initDosePerFxTB.Text = initNumFxTB.Text = initRxTB.Text = numOptLoops.Text = "";
            ClearAllItemsFromUIList(optimizationParamSP);
            ClearAllItemsFromUIList(planObjectiveParamSP);
        }

        private void populateRx()
        {
            //populate the prescription text boxes
            initDosePerFxTB.Text = plan.DosePerFraction.Dose.ToString();
            initNumFxTB.Text = plan.NumberOfFractions.ToString();
            initRxTB.Text = plan.TotalDose.Dose.ToString();
        }

        private void PopulateOptimizationTab(StackPanel theSP)
        {
            //clear the current list of optimization constraints and ones obtained from the plan to the user
            ClearAllItemsFromUIList(theSP);
            AddListItemsToUI(new OptimizationSetupUIHelper().ReadConstraintsFromPlan(plan), plan.Id, theSP);
        }

        private void PopulatePlanObjectivesTab(StackPanel theSP)
        {
            //clear the current list of optimization constraints and ones obtained from the plan to the user
            ClearAllItemsFromUIList(theSP);
            AddListItemsToUI(planObj, plan.Id, theSP);
        }

        private void AddOptimizationConstraintsHeader(StackPanel theSP)
        {
            theSP.Children.Add(new OptimizationSetupUIHelper().getOptHeader(theSP.Width));
        }

        private void AddPlanObjectivesHeader(StackPanel theSP)
        {
            theSP.Children.Add(new PlanObjectiveSetupUIHelper().GetObjHeader(theSP.Width));
        }

        private void AddListItemsToUI<T>(List<Tuple<string, string, double, double, T>> defaultList, string planId, StackPanel theSP)
        {
            int counter = 0;
            string clearBtnNamePrefix;
            OptimizationSetupUIHelper helper = new OptimizationSetupUIHelper();
            if (theSP.Name.ToLower().Contains("optimization"))
            {
                clearBtnNamePrefix = "clearOptimizationConstraintBtn";
                theSP.Children.Add(helper.AddPlanIdtoOptList(theSP, planId));
                AddOptimizationConstraintsHeader(theSP);
            }
            else
            {
                //do NOT add plan ID to plan objectives
                clearBtnNamePrefix = "clearPlanObjectiveBtn";
                //need special logic here because the entire stack panel is not cleared everytime a new item is added to the list
                if(theSP.Children.Count == 0) AddPlanObjectivesHeader(theSP);
            }
            for (int i = 0; i < defaultList.Count; i++)
            {
                counter++;
                theSP.Children.Add(helper.addOptVolume(theSP, 
                                                       selectedSS, 
                                                       defaultList[i], 
                                                       clearBtnNamePrefix, 
                                                       counter, 
                                                       new RoutedEventHandler(this.ClearItem_Click), 
                                                       theSP.Name.Contains("template") ? true : false));
            }
        }

        private void ClearAllItemsFromUIList(StackPanel theSP)
        {
            theSP.Children.Clear();
            if(!theSP.Name.ToLower().Contains("optimization"))
            {
                planObjectiveHeader.Background = System.Windows.Media.Brushes.PaleVioletRed;
                optimizationSetupHeader.Background = System.Windows.Media.Brushes.DarkGray;
            }
        }
        #endregion

        #region start optimization
        private void startOpt_Click(object sender, RoutedEventArgs e)
        {
            if (plan == null) 
            { 
                MessageBox.Show("No plan or course found!"); 
                return; 
            }

            if (optimizationParamSP.Children.Count == 0)
            {
                MessageBox.Show("No optimization parameters present to assign to the VMAT plan!");
                return;
            }
            if (!int.TryParse(numOptLoops.Text, out int numOptimizations))
            {
                MessageBox.Show("Error! Invalid input for number of optimization loops! \nFix and try again.");
                return;
            }

            if(!double.TryParse(targetNormTB.Text, out double planNorm))
            {
                MessageBox.Show("Error! Target normalization is NaN \nFix and try again.");
                return;
            }
            if(planNorm < 0.0 || planNorm > 100.0)
            {
                MessageBox.Show("Error! Target normalization is is either < 0% or > 100% \nExiting!");
                return;
            }

            List<Tuple<string, List<Tuple<string, string, double, double, int>>>> optParametersListList = new OptimizationSetupUIHelper().parseOptConstraints(optimizationParamSP);
            if (!optParametersListList.Any()) return;
            List<Tuple<string, string, double, double, DoseValuePresentation>> objectives = new PlanObjectiveSetupUIHelper().GetPlanObjectives(planObjectiveParamSP);
            if (!objectives.Any())
            {
                MessageBox.Show("Error! Missing plan objectives! Please add plan objectives and try again!");
                return;
            }
            //determine if flash was used to prep the plan
            if (optParametersListList.Where(x => x.Item2.Where(y => y.Item1.ToLower().Contains("flash")).Any()).Any()) useFlash = true;

            //does the user want to run the initial dose coverage check?
            runCoverageCheck = runCoverageCk.IsChecked.Value;
            //does the user want to run one additional optimization to reduce hotspots?
            runOneMoreOpt = runAdditionalOpt.IsChecked.Value;
            //does the user want to copy and save each plan after it's optimized (so the user can choose between the various plans)?
            copyAndSavePlanItr = copyAndSave.IsChecked.Value;

            //construct the actual plan objective array
            planDoseInfo = new List<Tuple<string, string, double, string>>(ConstructPlanDoseInfo());

            //create a new instance of the structure dataContainer and assign the optimization loop parameters entered by the user to the various data members
            dataContainer data = new dataContainer();
            data.construct(plan, 
                           optParametersListList.First().Item2, 
                           objectives, 
                           requestedTSstructures, 
                           planDoseInfo,
                           planNorm, 
                           numOptimizations, 
                           runCoverageCheck, 
                           runOneMoreOpt, 
                           copyAndSavePlanItr, 
                           useFlash, 
                           threshold, 
                           lowDoseLimit, 
                           demo, 
                           logFilePath, 
                           app);

            //start the optimization loop (all saving to the database is performed in the progressWindow class)
            pi.BeginModifications();
            //use a bit of polymorphism
            optimizationLoopBase optLoop;
            optLoop = new VMATCSIOptimization(data);
            optLoop.Execute();
        }

        private List<Tuple<string,string,double,string>> ConstructPlanDoseInfo()
        {
            List<Tuple<string, string, double, string>> tmp = new List<Tuple<string, string, double, string>> { };

            foreach(Tuple<string,string,double,string> itr in planDoseInfo)
            {
                if (itr.Item1 == "<targetId>")
                {
                    tmp.Add(Tuple.Create(GetPlanTargetId(), itr.Item2, itr.Item3, itr.Item4));
                }
                else
                {
                    tmp.Add(Tuple.Create(itr.Item1, itr.Item2, itr.Item3, itr.Item4));
                }
            }
            return tmp;
        }
           

        private List<Tuple<string, string, double, double, DoseValuePresentation>> ConstructPlanObjectives(List<Tuple<string, string, double, double, DoseValuePresentation>> obj)
        {
            List<Tuple<string, string, double, double, DoseValuePresentation>> tmp = new List<Tuple<string, string, double, double, DoseValuePresentation>> { };
            foreach(Tuple<string,string,double,double,DoseValuePresentation> itr in obj)
            {
                if(itr.Item1 == "<targetId>")
                {
                    tmp.Add(Tuple.Create(GetPlanTargetId(), itr.Item2, itr.Item3, itr.Item4, itr.Item5)); 
                }
                else
                {
                    if (selectedSS.Structures.Any(x => x.Id.ToLower() == itr.Item1.ToLower() && !x.IsEmpty)) tmp.Add(Tuple.Create(itr.Item1, itr.Item2, itr.Item3, itr.Item4, itr.Item5));
                }
            }
            return tmp;
        }

        private string GetPlanTargetId()
        {
            //if(useFlash) planObj.Add(Tuple.Create("TS_PTV_FLASH", obj.Item2, obj.Item3, obj.Item4, obj.Item5)); 
            //else planObj.Add(Tuple.Create("TS_PTV_VMAT", obj.Item2, obj.Item3, obj.Item4, obj.Item5)); 
            if (useFlash) return "TS_PTV_FLASH";
            else return "TS_PTV_CSI";
        }
        #endregion

        #region script configuration
        private void DisplayConfigurationParameters()
        {
            configTB.Text = "";
            configTB.Text = String.Format("{0}", DateTime.Now.ToString()) + Environment.NewLine;
            if (configFile != "") configTB.Text += String.Format("Configuration file: {0}", configFile) + Environment.NewLine + Environment.NewLine;
            else configTB.Text += String.Format("Configuration file: none") + Environment.NewLine + Environment.NewLine;
            configTB.Text += String.Format("Documentation path: {0}", documentationPath) + Environment.NewLine + Environment.NewLine;
            configTB.Text += String.Format("Log file path: {0}", logFilePath) + Environment.NewLine + Environment.NewLine;
            configTB.Text += String.Format("Default run parameters:") + Environment.NewLine;
            configTB.Text += String.Format("Demo mode: {0}", demo) + Environment.NewLine;
            configTB.Text += String.Format("Run coverage check: {0}", runCoverageCheckOption) + Environment.NewLine;
            configTB.Text += String.Format("Run additional optimization: {0}", runAdditionalOptOption) + Environment.NewLine;
            configTB.Text += String.Format("Copy and save each optimized plan: {0}", copyAndSaveOption) + Environment.NewLine;
            configTB.Text += String.Format("Plan normalization: {0}% (i.e., PTV V100% = {0}%)", defaultPlanNorm) + Environment.NewLine;
            configTB.Text += String.Format("Decision threshold: {0}", threshold) + Environment.NewLine;
            configTB.Text += String.Format("Relative lower dose limit: {0}", lowDoseLimit) + Environment.NewLine + Environment.NewLine;

            foreach (CSIAutoPlanTemplate itr in PlanTemplates.Where(x => x.templateName != "--select--"))
            {
                configTB.Text += "-----------------------------------------------------------------------------" + Environment.NewLine;

                configTB.Text += String.Format(" Template ID: {0}", itr.templateName) + Environment.NewLine;
                configTB.Text += String.Format(" Initial Dose per fraction: {0} cGy", itr.initialRxDosePerFx) + Environment.NewLine;
                configTB.Text += String.Format(" Initial number of fractions: {0}", itr.initialRxNumFx) + Environment.NewLine;
                configTB.Text += String.Format(" Boost Dose per fraction: {0} cGy", itr.boostRxDosePerFx) + Environment.NewLine;
                configTB.Text += String.Format(" Boost number of fractions: {0}", itr.boostRxNumFx) + Environment.NewLine;

                if (itr.targets.Any())
                {
                    configTB.Text += String.Format(" {0} targets:", itr.templateName) + Environment.NewLine;
                    configTB.Text += String.Format("  {0, -15} | {1, -8} | {2, -14} |", "structure Id", "Rx (cGy)", "Plan Id") + Environment.NewLine;
                    foreach (Tuple<string, double, string> tgt in itr.targets) configTB.Text += String.Format("  {0, -15} | {1, -8} | {2,-14:N1} |" + Environment.NewLine, tgt.Item1, tgt.Item2, tgt.Item3);
                    configTB.Text += Environment.NewLine;
                }
                else configTB.Text += String.Format(" No targets set for template: {0}", itr.templateName) + Environment.NewLine + Environment.NewLine;

                if (itr.TS_structures.Any())
                {
                    configTB.Text += String.Format(" {0} additional tuning structures:", itr.templateName) + Environment.NewLine;
                    configTB.Text += String.Format("  {0, -10} | {1, -15} |", "DICOM type", "Structure Id") + Environment.NewLine;
                    foreach (Tuple<string, string> ts in itr.TS_structures) configTB.Text += String.Format("  {0, -10} | {1, -15} |" + Environment.NewLine, ts.Item1, ts.Item2);
                    configTB.Text += Environment.NewLine;
                }
                else configTB.Text += String.Format(" No additional tuning structures for template: {0}", itr.templateName) + Environment.NewLine + Environment.NewLine;

                if (itr.spareStructures.Any())
                {
                    configTB.Text += String.Format(" {0} additional sparing structures:", itr.templateName) + Environment.NewLine;
                    configTB.Text += String.Format("  {0, -15} | {1, -26} | {2, -11} |", "structure Id", "sparing type", "margin (cm)") + Environment.NewLine;
                    foreach (Tuple<string, string, double> spare in itr.spareStructures) configTB.Text += String.Format("  {0, -15} | {1, -26} | {2,-11:N1} |" + Environment.NewLine, spare.Item1, spare.Item2, spare.Item3);
                    configTB.Text += Environment.NewLine;
                }
                else configTB.Text += String.Format(" No additional sparing structures for template: {0}", itr.templateName) + Environment.NewLine + Environment.NewLine;

                if (itr.init_constraints.Any())
                {
                    configTB.Text += String.Format(" {0} template initial plan optimization parameters:", itr.templateName) + Environment.NewLine;
                    configTB.Text += String.Format("  {0, -15} | {1, -16} | {2, -10} | {3, -10} | {4, -8} |", "structure Id", "constraint type", "dose (cGy)", "volume (%)", "priority") + Environment.NewLine;
                    foreach (Tuple<string, string, double, double, int> opt in itr.init_constraints) configTB.Text += String.Format("  {0, -15} | {1, -16} | {2,-10:N1} | {3,-10:N1} | {4,-8} |" + Environment.NewLine, opt.Item1, opt.Item2, opt.Item3, opt.Item4, opt.Item5);
                    configTB.Text += Environment.NewLine;
                }
                else configTB.Text += String.Format(" No iniital plan optimization constraints for template: {0}", itr.templateName) + Environment.NewLine + Environment.NewLine;

                if (itr.bst_constraints.Any())
                {
                    configTB.Text += String.Format(" {0} template boost plan optimization parameters:", itr.templateName) + Environment.NewLine;
                    configTB.Text += String.Format("  {0, -15} | {1, -16} | {2, -10} | {3, -10} | {4, -8} |", "structure Id", "constraint type", "dose (cGy)", "volume (%)", "priority") + Environment.NewLine;
                    foreach (Tuple<string, string, double, double, int> opt in itr.bst_constraints) configTB.Text += String.Format("  {0, -15} | {1, -16} | {2,-10:N1} | {3,-10:N1} | {4,-8} |" + Environment.NewLine, opt.Item1, opt.Item2, opt.Item3, opt.Item4, opt.Item5);
                    configTB.Text += Environment.NewLine;
                }
                else configTB.Text += String.Format(" No boost plan optimization constraints for template: {0}", itr.templateName) + Environment.NewLine + Environment.NewLine;

                if (itr.planDoseInfo.Any())
                {
                    configTB.Text += String.Format(" {0} template requested dosimetric info after each iteration:", itr.templateName) + Environment.NewLine;
                    configTB.Text += String.Format(" {0, -15} | {1, -6} | {2, -9} |", "structure Id", "metric", "dose type") + Environment.NewLine;

                    foreach (Tuple<string, string, double, string> info in itr.planDoseInfo)
                    {
                        if (info.Item2.Contains("max") || info.Item2.Contains("min")) configTB.Text += String.Format(" {0, -15} | {1, -6} | {2, -9} |", info.Item1, info.Item2, info.Item4) + Environment.NewLine;
                        else configTB.Text += String.Format(" {0, -15} | {1, -6} | {2, -9} |", info.Item1, String.Format("{0}{1}%", info.Item2, info.Item3), info.Item4) + Environment.NewLine;
                    }
                    configTB.Text += Environment.NewLine;
                    configTB.Text += Environment.NewLine;
                }
                else configTB.Text += String.Format(" No requested dosimetric info for template: {0}", itr.templateName) + Environment.NewLine + Environment.NewLine;

                if(itr.planObj.Any())
                {
                    configTB.Text += String.Format(" {0} template plan objectives:", itr.templateName) + Environment.NewLine;
                    configTB.Text += String.Format(" {0, -15} | {1, -16} | {2, -10} | {3, -10} | {4, -9} |", "structure Id", "constraint type", "dose", "volume (%)", "dose type") + Environment.NewLine;
                    foreach (Tuple<string, string, double, double, DoseValuePresentation> obj in itr.planObj)
                    {
                        configTB.Text += String.Format(" {0, -15} | {1, -16} | {2,-10:N1} | {3,-10:N1} | {4,-9} |" + Environment.NewLine, obj.Item1, obj.Item2, obj.Item3, obj.Item4, obj.Item5);
                    }
                    configTB.Text += Environment.NewLine;
                }
                else configTB.Text += String.Format(" No plan objectives for template: {0}", itr.templateName) + Environment.NewLine + Environment.NewLine;

                if(itr.requestedTSstructures.Any())
                {
                    configTB.Text += String.Format(" {0} template requested tuning structures:", itr.templateName) + Environment.NewLine;
                    configTB.Text += String.Format(" {0, -15} | {1, -9} | {2, -10} | {3, -5} | {4, -8} | {5, -10} |", "structure Id", "low D (%)", "high D (%)", "V (%)", "priority", "constraint") + Environment.NewLine;
                    foreach (Tuple<string, double, double, double, int, List<Tuple<string, double, string, double>>> ts in itr.requestedTSstructures)
                    {
                        configTB.Text += String.Format(" {0, -15} | {1, -9:N1} | {2,-10:N1} | {3,-5:N1} | {4,-8} |", ts.Item1, ts.Item2, ts.Item3, ts.Item4, ts.Item5);
                        if (!ts.Item6.Any()) configTB.Text += String.Format(" {0,-10} |", "none") + Environment.NewLine;
                        else
                        {
                            int count = 0;
                            foreach (Tuple<string, double, string, double> ts1 in ts.Item6)
                            {
                                if (count == 0)
                                {
                                    if (ts1.Item1.Contains("Dmax")) configTB.Text += String.Format(" {0,-10} |", String.Format("{0}{1}{2}%", ts1.Item1, ts1.Item3, ts1.Item4)) + Environment.NewLine;
                                    else if (ts1.Item1.Contains("V")) configTB.Text += String.Format(" {0,-10} |", String.Format("{0}{1}%{2}{3}%", ts1.Item1, ts1.Item2, ts1.Item3, ts1.Item4)) + Environment.NewLine;
                                    else configTB.Text += String.Format(" {0,-10} |", String.Format("{0}", ts1.Item1)) + Environment.NewLine;
                                }
                                else
                                {
                                    if (ts1.Item1.Contains("Dmax")) configTB.Text += String.Format(" {0,-59} | {1,-10} |", " ", String.Format("{0}{1}{2}%", ts1.Item1, ts1.Item3, ts1.Item4)) + Environment.NewLine;
                                    else if (ts1.Item1.Contains("V")) configTB.Text += String.Format(" {0,-59} | {1,-10} |", " ", String.Format("{0}{1}%{2}{3}%", ts1.Item1, ts1.Item2, ts1.Item3, ts1.Item4)) + Environment.NewLine;
                                    else configTB.Text += String.Format(" {0,-59} | {1,-10} |", " ", String.Format("{0}", ts1.Item1)) + Environment.NewLine;
                                }
                                count++;
                            }
                        }
                    }
                    configTB.Text += Environment.NewLine;
                }
                else configTB.Text += String.Format(" No requested heater/cooler structures for template: {0}", itr.templateName) + Environment.NewLine + Environment.NewLine;
            }
            configScroller.ScrollToTop();
        }

        private void loadNewConfigFile_Click(object sender, RoutedEventArgs e)
        {
            configFile = "";
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.InitialDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\configuration\\";
            openFileDialog.Filter = "ini files (*.ini)|*.ini|All files (*.*)|*.*";

            if (openFileDialog.ShowDialog().Value) 
            { 
                if (!loadConfigurationSettings(openFileDialog.FileName)) 
                { 
                    if (pi != null) DisplayConfigurationParameters(); 
                } 
                else MessageBox.Show("Error! Selected file is NOT valid!"); 
            }
        }

        private bool loadConfigurationSettings(string file)
        {
            configFile = file;
            ConfigurationHelper helper = new ConfigurationHelper();
            try
            {
                using (StreamReader reader = new StreamReader(configFile))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!string.IsNullOrEmpty(line) && line.Substring(0, 1) != "%")
                        {
                            //start actually reading data when you find the begin executable configuration tab
                            if (line.Equals(":begin executable configuration:"))
                            {
                                while (!(line = reader.ReadLine()).Equals(":end executable configuration:"))
                                {
                                    if (!string.IsNullOrEmpty(line) && line.Substring(0, 1) != "%")
                                    {
                                        //useful info on this line
                                        if (line.Contains("="))
                                        {
                                            string parameter = line.Substring(0, line.IndexOf("="));
                                            string value = line.Substring(line.IndexOf("=") + 1, line.Length - line.IndexOf("=") - 1);
                                            if (double.TryParse(value, out double result))
                                            {
                                                if (parameter == "default number of optimizations") defautlNumOpt = value;
                                                else if (parameter == "default plan normalization") defaultPlanNorm = value;
                                                else if (parameter == "decision threshold") threshold = result;
                                                else if (parameter == "relative lower dose limit") lowDoseLimit = result;
                                            }
                                            else if (parameter == "documentation path")
                                            {
                                                documentationPath = value;
                                                if (documentationPath.LastIndexOf("\\") != documentationPath.Length - 1) documentationPath += "\\";
                                            }
                                            else if (parameter == "log file path")
                                            {
                                                logFilePath = value;
                                                if (logFilePath.LastIndexOf("\\") != logFilePath.Length - 1) logFilePath += "\\";
                                            }
                                            else if (parameter == "demo") { if (value != "") demo = bool.Parse(value); }
                                            else if (parameter == "run coverage check") { if (value != "") runCoverageCheckOption = bool.Parse(value); }
                                            else if (parameter == "run additional optimization") { if (value != "") runAdditionalOptOption = bool.Parse(value); }
                                            else if (parameter == "copy and save each plan") { if (value != "") copyAndSaveOption = bool.Parse(value); }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    
                }
                int count = 1;
                foreach (string itr in Directory.GetFiles(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\templates\\", "*.ini").OrderBy(x => x))
                {
                    PlanTemplates.Add(helper.readTemplatePlan(itr, count++));
                }
                return false;
            }
            catch (Exception e) { MessageBox.Show(String.Format("Error could not load configuration file because: {0}\n\nAssuming default parameters", e.Message)); return true; }
        }
        #endregion

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //be sure to close the patient before closing the application. Not doing so will result in unclosed timestamps in eclipse
            app.ClosePatient();
            app.Dispose();
        }

    }
}
