﻿using System;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.Windows.Forms;
using System.Windows.Media.Media3D;
using VMATTBICSIAutoplanningHelpers.Prompts;
using VMATTBICSIAutoplanningHelpers.Helpers;
using SimpleProgressWindow;

namespace VMATTBICSIAutoplanningHelpers.BaseClasses
{
    public class PlaceBeamsBase : SimpleMTbase
    {
        public List<ExternalPlanSetup> GetGeneratedPlans() { return plans; }
        public List<Tuple<ExternalPlanSetup, List<Structure>>> GetFieldJunctionStructures() { return jnxs; }

        protected bool contourOverlap = false;
        protected bool checkIsoPlacement = false;
        protected List<ExternalPlanSetup> plans = new List<ExternalPlanSetup> { };
        protected double checkIsoPlacementLimit = 5.0;
        private string courseId;
        protected Course theCourse;
        protected StructureSet selectedSS;
        //plan ID, target Id, numFx, dosePerFx, cumulative dose
        protected List<Tuple<string, string, int, DoseValue, double>> prescriptions;
        protected string calculationModel = "";
        protected string optimizationModel = "";
        protected string useGPUdose = "";
        protected string useGPUoptimization = "";
        protected string MRrestart = "";
        protected double contourOverlapMargin;
        protected List<Tuple<ExternalPlanSetup,List<Structure>>> jnxs = new List<Tuple<ExternalPlanSetup, List<Structure>>> { };
        protected Structure target = null;
        protected int numVMATIsos;

        #region virtual methods
        protected virtual bool GeneratePlanList()
        {
            if (CheckExistingCourse()) return true;
            if (CheckExistingPlans()) return true;
            if (CreatePlans()) return true;
            //plan, List<isocenter position, isocenter name, number of beams per isocenter>
            List<Tuple<ExternalPlanSetup, List<Tuple<VVector, string, int>>>> isoLocations = GetIsocenterPositions();
            UpdateUILabel("Assigning isocenters and beams: ");
            int isoCount = 0;
            foreach(Tuple<ExternalPlanSetup, List<Tuple<VVector, string, int>>> itr in isoLocations)
            {
                if(SetBeams(itr)) return true;
                //ensure contour overlap is requested AND there are more than two isocenters for this plan
                if (contourOverlap && itr.Item2.Count > 1) if (ContourFieldOverlap(itr, isoCount)) return true;
                isoCount += itr.Item2.Count;
            }
            UpdateUILabel("Finished!");

            if (checkIsoPlacement) MessageBox.Show(String.Format("WARNING: < {0:0.00} cm margin at most superior and inferior locations of body! Verify isocenter placement!", checkIsoPlacementLimit / 10));
            return false;
        }

        //2-12-2023 to be converted to non-virtual method so TBI uses the same plan checking syntax as CSI
        protected virtual bool CheckExistingPlans()
        {
            UpdateUILabel("Checking for existing plans: ");
            int numExistingPlans = 0;
            int calcItems = prescriptions.Count;
            int counter = 0;
            foreach (Tuple<string, string, int, DoseValue, double> itr in prescriptions)
            {
                if (theCourse.ExternalPlanSetups.Where(x => x.Id == itr.Item1).Any())
                {
                    numExistingPlans++;
                    ProvideUIUpdate((int)(100 * ++counter / calcItems), String.Format("Plan {0} EXISTS in course {1}", itr.Item1, courseId));
                }
                else ProvideUIUpdate((int)(100 * ++counter / calcItems), String.Format("Plan {0} does not exist in course {1}", itr.Item1, courseId));
            }
            if (numExistingPlans > 0)
            {
                ProvideUIUpdate(0, String.Format("One or more plans exist in course {0}", courseId));
                ProvideUIUpdate(String.Format("ESAPI can't remove plans in the clinical environment!"));
                ProvideUIUpdate(String.Format("Please manually remove this plan and try again.", true));
                return true;
            }
            else ProvideUIUpdate(100, String.Format("No plans currently exist in course {0}!", courseId));

            return false;
        }

        protected virtual bool SetBeams(Tuple<ExternalPlanSetup, List<Tuple<VVector, string, int>>> isoLocations)
        {
            //needs to be implemented by deriving class
            return true;
        }

        protected virtual List<Tuple<ExternalPlanSetup, List<Tuple<VVector, string, int>>>> GetIsocenterPositions()
        {
            return new List<Tuple<ExternalPlanSetup, List<Tuple<VVector, string, int>>>> { };
        }
        #endregion

        #region concrete methods
        public void Initialize(string cId, List<Tuple<string, string, int, DoseValue, double>> presc)
        {
            courseId = cId;
            prescriptions = new List<Tuple<string, string, int, DoseValue, double>>(presc);
        }

        private bool CheckExistingCourse()
        {
            UpdateUILabel("Check course: ");
            int calcItems = 1;
            int counter = 0;
            ProvideUIUpdate(0, String.Format("Checking for existing course {0}", courseId));
            //look for a course with id = courseId assigned at initialization. If it does not exit, create it, otherwise load it into memory
            if (selectedSS.Patient.Courses.Where(x => x.Id == courseId).Any())
            {
                ProvideUIUpdate((int)(100 * ++counter / calcItems), String.Format("Course {0} found!", courseId));
                theCourse = selectedSS.Patient.Courses.FirstOrDefault(x => x.Id == courseId);
            }
            else
            {
                ProvideUIUpdate((int)(100 * ++counter / calcItems), String.Format("Course {0} does not exist. Creating now!", courseId));
                theCourse = CreateCourse();
            }
            if (theCourse == null)
            {
                ProvideUIUpdate(0, String.Format("Course creation or assignment failed! Exiting!", true));
                return true;
            }
            ProvideUIUpdate(100, String.Format("Course {0} retrieved!", courseId));
            return false;
        }

        private Course CreateCourse()
        {
            Course tmpCourse = null;
            if (selectedSS.Patient.CanAddCourse())
            {
                tmpCourse = selectedSS.Patient.AddCourse();
                tmpCourse.Id = courseId;
            }
            else
            {
                ProvideUIUpdate(String.Format("Error! Can't add a treatment course to the patient!"));
            }
            return tmpCourse;
        }

        private bool CreatePlans()
        {
            UpdateUILabel("Creating plans: ");
            foreach (Tuple<string, string, int, DoseValue, double> itr in prescriptions)
            {
                int calcItems = 9 * prescriptions.Count;
                int counter = 0;
                ProvideUIUpdate(0, String.Format("Creating plan {0}", itr.Item1));
                ExternalPlanSetup thePlan = theCourse.AddExternalPlanSetup(selectedSS);
                ProvideUIUpdate((int)(100 * ++counter / calcItems), String.Format("Created plan {0}", itr.Item1));
                //100% dose prescribed in plan and plan ID is in the prescriptions
                thePlan.SetPrescription(itr.Item3, itr.Item4, 1.0);
                ProvideUIUpdate((int)(100 * ++counter / calcItems), String.Format("Set prescription for plan {0}", itr.Item1));

                string planName = itr.Item1;
                thePlan.Id = planName;
                ProvideUIUpdate((int)(100 * ++counter / calcItems), String.Format("Set plan Id for {0}", itr.Item1));

                //ask the user to set the calculation model if not calculation model was set in UI.xaml.cs (up near the top with the global parameters)
                if (calculationModel == "")
                {
                    selectItem SUI = new selectItem();
                    SUI.title.Text = "No calculation model set!" + Environment.NewLine + "Please select a calculation model!";
                    foreach (string s in thePlan.GetModelsForCalculationType(CalculationType.PhotonVolumeDose)) SUI.itemCombo.Items.Add(s);
                    SUI.ShowDialog();
                    if (!SUI.confirm) return true;
                    //get the plan the user chose from the combobox
                    calculationModel = SUI.itemCombo.SelectedItem.ToString();

                    //just an FYI that the calculation will likely run out of memory and crash the optimization when Acuros is used
                    if (calculationModel.ToLower().Contains("acuros") || calculationModel.ToLower().Contains("axb"))
                    {
                        confirmUI CUI = new confirmUI();
                        CUI.message.Text = "Warning!" + Environment.NewLine + "The optimization will likely crash (i.e., run out of memory) if Acuros is used!" + Environment.NewLine + "Continue?!";
                        CUI.ShowDialog();
                        if (!CUI.confirm) return true;
                    }
                }

                thePlan.SetCalculationModel(CalculationType.PhotonVolumeDose, calculationModel);
                ProvideUIUpdate((int)(100 * ++counter / calcItems), String.Format("Set calculation model to {0}", calculationModel));

                thePlan.SetCalculationModel(CalculationType.PhotonVMATOptimization, optimizationModel);
                ProvideUIUpdate((int)(100 * ++counter / calcItems), String.Format("Set optimization model to {0}", optimizationModel));

                //Dictionary<string, string> d = thePlan.GetCalculationOptions(thePlan.GetCalculationModel(CalculationType.PhotonVMATOptimization));
                //string m = "";
                //foreach (KeyValuePair<string, string> t in d) m += String.Format("{0}, {1}", t.Key, t.Value) + System.Environment.NewLine;
                //MessageBox.Show(m);

                //set the GPU dose calculation option (only valid for acuros)
                if (useGPUdose == "Yes" && !calculationModel.Contains("AAA"))
                {
                    thePlan.SetCalculationOption(calculationModel, "UseGPU", useGPUdose);
                    ProvideUIUpdate((int)(100 * ++counter / calcItems), String.Format("Set GPU option for dose calc to {0}", useGPUdose));
                }
                else
                {
                    thePlan.SetCalculationOption(calculationModel, "UseGPU", "No");
                    ProvideUIUpdate((int)(100 * ++counter / calcItems), String.Format("Set GPU option for dose calculation to {0}", "No"));
                }


                //set MR restart level option for the photon optimization
                thePlan.SetCalculationOption(optimizationModel, "VMAT/MRLevelAtRestart", MRrestart);
                ProvideUIUpdate((int)(100 * ++counter / calcItems), String.Format("Set MR Restart level to {0}", MRrestart));

                //set the GPU optimization option
                if (useGPUoptimization == "Yes")
                {
                    thePlan.SetCalculationOption(optimizationModel, "General/OptimizerSettings/UseGPU", useGPUoptimization);
                    ProvideUIUpdate((int)(100 * ++counter / calcItems), String.Format("Set GPU option for optimization to {0}", useGPUoptimization));
                }
                else
                {
                    thePlan.SetCalculationOption(optimizationModel, "General/OptimizerSettings/UseGPU", "No");
                    ProvideUIUpdate((int)(100 * ++counter / calcItems), String.Format("Set GPU option for optimization to {0}", "No"));
                }

                //reference point can only be added for a plan that IS CURRENTLY OPEN
                //plan.AddReferencePoint(selectedSS.Structures.First(x => x.Id == "TS_PTV_VMAT"), null, "VMAT TBI", "VMAT TBI");

                //these need to be fixed
                //v16 of Eclipse allows for the creation of a plan with a named target structure and named primary reference point. Neither of these options are available in v15
                //plan.TargetVolumeID = selectedSS.Structures.First(x => x.Id == "xx");
                //plan.PrimaryReferencePoint = plan.ReferencePoints.Fisrt(x => x.Id == "xx");
                plans.Add(thePlan);
                ProvideUIUpdate((int)(100 * ++counter / calcItems), String.Format("Added plan {0} to stack!", itr.Item1));
            }
            ProvideUIUpdate(100, String.Format("Finished creating and initializing plans!"));
            return false;
        }

        //function used to contour the overlap between fields in adjacent isocenters for the VMAT Plans ONLY!
        //this option is requested by the user by selecting the checkbox on the main UI on the beam placement tab
        private bool ContourFieldOverlap(Tuple<ExternalPlanSetup, List<Tuple<VVector, string, int>>> isoLocations, int isoCount)
        {
            UpdateUILabel("Contour field overlap:");
            int percentCompletion = 0;
            int calcItems = 3 + 7 * isoLocations.Item2.Count - 1;
            //grab target Id for this prescription item
            if(prescriptions.FirstOrDefault(y => y.Item1 == isoLocations.Item1.Id) == null)
            {
                ProvideUIUpdate(String.Format("Error! No matching prescrition found for iso plan name {0}", isoLocations.Item1.Id), true);
                return true;
            }
            string targetId = prescriptions.FirstOrDefault(y => y.Item1 == isoLocations.Item1.Id).Item2;
            Structure target_tmp = selectedSS.Structures.FirstOrDefault(x => x.Id == targetId);
            if (target_tmp == null)
            {
                ProvideUIUpdate(String.Format("Error getting target structure ({0}) for plan: {1}! Exiting!", targetId, prescriptions.FirstOrDefault(y => y.Item1 == isoLocations.Item1.Id)), true);
                return true;
            }
            ProvideUIUpdate((int)(100 * ++percentCompletion / calcItems), String.Format("Retrieved target: {0} for plan: {1}", target_tmp.Id, isoLocations.Item1.Id));
            //grab the image and get the z resolution and dicom origin (we only care about the z position of the dicom origin)
            Image image = selectedSS.Image;
            double zResolution = image.ZRes;
            VVector dicomOrigin = image.Origin;
            ProvideUIUpdate(String.Format("Retrived image: {0}", image.Id));
            ProvideUIUpdate(String.Format("Z resolution: {0} mm", zResolution));
            ProvideUIUpdate(String.Format("DICOM origin: ({0}, {1}, {2}) mm", dicomOrigin.x, dicomOrigin.y, dicomOrigin.z));

            //center position between adjacent isocenters, number of image slices to contour on, start image slice location for contouring
            List<Tuple<double, int, int>> overlap = new List<Tuple<double, int, int>> { };
            //calculate the center position between adjacent isocenters, number of image slices to contour on based on overlap and with additional user-specified margin (from main UI)
            //and the slice where the contouring should begin
            List<Structure> tmpJnxList = new List<Structure> { };
            for (int i = 1; i < isoLocations.Item2.Count; i++)
            {
                ProvideUIUpdate(String.Format("Junction: {0}", i));
                //this is left as a double so I can cast it to an int in the second overlap item and use it in the calculation in the third overlap item
                //logic to consider the situation where the y extent of the fields are NOT 40 cm!
                Beam iso1Beam1 = isoLocations.Item1.Beams.First(x => CalculationHelper.AreEqual(x.IsocenterPosition.z, isoLocations.Item2.ElementAt(i - 1).Item1.z));
                ProvideUIUpdate((int)(100 * ++percentCompletion / calcItems), String.Format("First beam in isocenter {0}: {1}", i - 1, iso1Beam1.Id));

                Beam iso2Beam1 = isoLocations.Item1.Beams.First(x => CalculationHelper.AreEqual(x.IsocenterPosition.z, isoLocations.Item2.ElementAt(i).Item1.z));
                ProvideUIUpdate((int)(100 * ++percentCompletion / calcItems), String.Format("First beam in isocenter {0}: {1}", i, iso2Beam1.Id));

                //assumes iso1beam1 y1 is oriented inferior on patient and iso2beam1 is oriented superior on patient
                double fieldLength = Math.Abs(iso1Beam1.GetEditableParameters().ControlPoints.First().JawPositions.Y1) + Math.Abs(iso2Beam1.GetEditableParameters().ControlPoints.First().JawPositions.Y2);
                ProvideUIUpdate((int)(100 * ++percentCompletion / calcItems), String.Format("Field length ({0} Y1 + {1} Y2): {2} mm", iso1Beam1.Id, iso2Beam1.Id, fieldLength));

                double numSlices = Math.Ceiling(fieldLength + contourOverlapMargin - Math.Abs(isoLocations.Item2.ElementAt(i).Item1.z - isoLocations.Item2.ElementAt(i - 1).Item1.z));
                ProvideUIUpdate((int)(100 * ++percentCompletion / calcItems), String.Format("Number of slices to contour: {0}", (int)(numSlices / zResolution)));

                //calculate the center position between adjacent isocenters. NOTE: this calculation works from superior to inferior!
                double overlapCenter = isoLocations.Item2.ElementAt(i - 1).Item1.z + iso1Beam1.GetEditableParameters().ControlPoints.First().JawPositions.Y1  - contourOverlapMargin / 2 + numSlices / 2;
                ProvideUIUpdate((int)(100 * ++percentCompletion / calcItems), String.Format("Overlap center position: {0} mm", overlapCenter));

                overlap.Add(new Tuple<double, int, int>(overlapCenter, // the center location
                                                        (int)(numSlices / zResolution), //total number of slices to contour
                                                        (int)(Math.Abs(dicomOrigin.z - overlapCenter + numSlices / 2) / zResolution))); // starting slice to contour
                ProvideUIUpdate((int)(100 * ++percentCompletion / calcItems), String.Format("Starting slice to contour: {0}", (int)(Math.Abs(dicomOrigin.z - overlapCenter + numSlices / 2) / zResolution)));
                //add a new junction structure (named TS_jnx<i>) to the stack. Contours will be added to these structure later
                tmpJnxList.Add(selectedSS.AddStructure("CONTROL", string.Format("TS_jnx{0}", isoCount + i)));
                ProvideUIUpdate((int)(100 * ++percentCompletion / calcItems), String.Format("Added TS junction to stack: TS_jnx{0}", isoCount + 1));
            }
            jnxs.Add(Tuple.Create(isoLocations.Item1, tmpJnxList));

            //make a box at the min/max x,y positions of the target structure with no margin
            VVector[] targetBoundingBox = CreateTargetBoundingBox(target_tmp, 0.0);
            ProvideUIUpdate((int)(100 * ++percentCompletion / calcItems), String.Format("Created target bounding box for contouring overlap"));
           
            //add the contours to each relevant plan for each structure in the jnxs stack
            int count = 0;
            foreach (Tuple<double, int, int> value in overlap)
            {
                percentCompletion = 0;
                calcItems = value.Item2;
                ProvideUIUpdate(0, String.Format("Contouring junction: {0}", tmpJnxList.ElementAt(count).Id));
                for (int i = value.Item3; i < (value.Item3 + value.Item2); i++)
                {
                    tmpJnxList.ElementAt(count).AddContourOnImagePlane(targetBoundingBox, i);
                    ProvideUIUpdate((int)(100 * ++percentCompletion / calcItems));
                }
                //only keep the portion of the box contour that overlaps with the target
                tmpJnxList.ElementAt(count).SegmentVolume = tmpJnxList.ElementAt(count).And(target_tmp.Margin(0));
                count++;
            }
            return false;
        }

        private VVector[] CreateTargetBoundingBox(Structure target, double margin)
        {
            margin *= 10;
            //margin is in cm
            Point3DCollection targetPts = target.MeshGeometry.Positions;
            double xMax = targetPts.Max(p => p.X) + margin;
            double xMin = targetPts.Min(p => p.X) - margin;
            double yMax = targetPts.Max(p => p.Y) + margin;
            double yMin = targetPts.Min(p => p.Y) - margin;

            VVector[] pts = new[] {
                                    new VVector(xMax, yMax, 0),
                                    new VVector(xMax, 0, 0),
                                    new VVector(xMax, yMin, 0),
                                    new VVector(0, yMin, 0),
                                    new VVector(xMin, yMin, 0),
                                    new VVector(xMin, 0, 0),
                                    new VVector(xMin, yMax, 0),
                                    new VVector(0, yMax, 0)};

            return pts;
        }
        #endregion
    }
}
