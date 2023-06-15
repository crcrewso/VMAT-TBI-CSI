﻿using System;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.Windows.Forms;
using System.Windows.Media.Media3D;
using VMATTBICSIAutoPlanningHelpers.Prompts;
using VMATTBICSIAutoPlanningHelpers.Helpers;
using SimpleProgressWindow;

namespace VMATTBICSIAutoPlanningHelpers.BaseClasses
{
    public class PlaceBeamsBase : SimpleMTbase
    {
        public List<ExternalPlanSetup> GetGeneratedVMATPlans() { return vmatPlans; }
        public List<Tuple<ExternalPlanSetup, List<Structure>>> GetFieldJunctionStructures() { return jnxs; }
        public string GetErrorStackTrace() { return stackTraceError; }

        protected bool contourOverlap = false;
        protected bool checkIsoPlacement = false;
        protected List<ExternalPlanSetup> vmatPlans = new List<ExternalPlanSetup> { };
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
        protected string stackTraceError;

        #region virtual methods
        //2-12-2023 to be converted to non-virtual method so TBI uses the same plan checking syntax as CSI
        protected virtual bool CheckExistingPlans()
        {
            UpdateUILabel("Checking for existing plans: ");
            int numExistingPlans = 0;
            int calcItems = prescriptions.Count;
            int counter = 0;
            foreach (Tuple<string, string, int, DoseValue, double> itr in prescriptions)
            {
                if (theCourse.ExternalPlanSetups.Where(x => string.Equals(x.Id, itr.Item1)).Any())
                {
                    numExistingPlans++;
                    ProvideUIUpdate((int)(100 * ++counter / calcItems), $"Plan {itr.Item1} EXISTS in course {courseId}");
                }
                else ProvideUIUpdate((int)(100 * ++counter / calcItems), $"Plan {itr.Item1} does not exist in course {courseId}");
            }
            if (numExistingPlans > 0)
            {
                ProvideUIUpdate(0, $"One or more plans exist in course {courseId}");
                ProvideUIUpdate("ESAPI can't remove plans in the clinical environment!");
                ProvideUIUpdate("Please manually remove this plan and try again.", true);
                return true;
            }
            else ProvideUIUpdate(100, $"No plans currently exist in course {courseId}!");
            ProvideUIUpdate($"Elapsed time: {GetElapsedTime()}");
            return false;
        }

        protected virtual bool SetVMATBeams(Tuple<ExternalPlanSetup, List<Tuple<VVector, string, int>>> isoLocations)
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

        protected bool CheckExistingCourse()
        {
            UpdateUILabel("Check course: ");
            int calcItems = 1;
            int counter = 0;
            ProvideUIUpdate(0, $"Checking for existing course {courseId}");
            //look for a course with id = courseId assigned at initialization. If it does not exit, create it, otherwise load it into memory
            if (selectedSS.Patient.Courses.Any(x => string.Equals(x.Id, courseId)))
            {
                ProvideUIUpdate((int)(100 * ++counter / calcItems), $"Course {courseId} found!");
                theCourse = selectedSS.Patient.Courses.FirstOrDefault(x => string.Equals(x.Id, courseId));
            }
            else
            {
                ProvideUIUpdate((int)(100 * ++counter / calcItems), $"Course  {courseId} does not exist. Creating now!");
                theCourse = CreateCourse();
            }
            if (theCourse == null)
            {
                ProvideUIUpdate(0, "Course creation or assignment failed! Exiting!", true);
                return true;
            }
            ProvideUIUpdate(100, $"Course {courseId} retrieved!");
            ProvideUIUpdate($"Elapsed time: {GetElapsedTime()}");
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
                ProvideUIUpdate("Error! Can't add a treatment course to the patient!");
            }
            return tmpCourse;
        }

        protected bool CreateVMATPlans()
        {
            UpdateUILabel("Creating VMAT plans: ");
            foreach (Tuple<string, string, int, DoseValue, double> itr in prescriptions)
            {
                int calcItems = 9 * prescriptions.Count;
                int counter = 0;
                ProvideUIUpdate(0, $"Creating plan {itr.Item1}");
                ExternalPlanSetup thePlan = theCourse.AddExternalPlanSetup(selectedSS);
                ProvideUIUpdate((int)(100 * ++counter / calcItems), $"Created plan {itr.Item1}");
                //100% dose prescribed in plan and plan ID is in the prescriptions
                thePlan.SetPrescription(itr.Item3, itr.Item4, 1.0);
                ProvideUIUpdate((int)(100 * ++counter / calcItems), $"Set prescription for plan {itr.Item1}");

                string planName = itr.Item1;
                thePlan.Id = planName;
                ProvideUIUpdate((int)(100 * ++counter / calcItems), $"Set plan Id for {itr.Item1}");

                //ask the user to set the calculation model if not calculation model was set in UI.xaml.cs (up near the top with the global parameters)
                if (calculationModel == "")
                {
                    SelectItemPrompt SIP = new SelectItemPrompt("No calculation model set!" + Environment.NewLine + "Please select a calculation model!", thePlan.GetModelsForCalculationType(CalculationType.PhotonVolumeDose).ToList());
                    SIP.ShowDialog();
                    if (!SIP.GetSelection()) return true;
                    //get the plan the user chose from the combobox
                    calculationModel = SIP.GetSelectedItem();

                    //just an FYI that the calculation will likely run out of memory and crash the optimization when Acuros is used
                    if (calculationModel.ToLower().Contains("acuros") || calculationModel.ToLower().Contains("axb"))
                    {
                        ConfirmPrompt CP = new ConfirmPrompt("Warning!" + Environment.NewLine + "The optimization will likely crash (i.e., run out of memory) if Acuros is used!" + Environment.NewLine + "Continue?!");
                        CP.ShowDialog();
                        if (!CP.GetSelection()) return true;
                    }
                }

                thePlan.SetCalculationModel(CalculationType.PhotonVolumeDose, calculationModel);
                ProvideUIUpdate((int)(100 * ++counter / calcItems), $"Set calculation model to {calculationModel}");

                thePlan.SetCalculationModel(CalculationType.PhotonVMATOptimization, optimizationModel);
                ProvideUIUpdate((int)(100 * ++counter / calcItems), $"Set optimization model to {optimizationModel}");

                Dictionary<string, string> d = thePlan.GetCalculationOptions(thePlan.GetCalculationModel(CalculationType.PhotonVMATOptimization));
                ProvideUIUpdate($"Calculation options for {optimizationModel}:");
                foreach (KeyValuePair<string, string> t in d) ProvideUIUpdate($"{t.Key}, {t.Value}");

                //set the GPU dose calculation option (only valid for acuros)
                if (useGPUdose == "Yes" && !calculationModel.Contains("AAA"))
                {
                    thePlan.SetCalculationOption(calculationModel, "UseGPU", useGPUdose);
                    ProvideUIUpdate((int)(100 * ++counter / calcItems), $"Set GPU option for dose calc to {useGPUdose}");
                }
                else
                {
                    thePlan.SetCalculationOption(calculationModel, "UseGPU", "No");
                    ProvideUIUpdate((int)(100 * ++counter / calcItems), "Set GPU option for dose calculation to No");
                }

                //set MR restart level option for the photon optimization
                if(!thePlan.SetCalculationOption(optimizationModel, "MRLevelAtRestart", MRrestart))
                {
                    ProvideUIUpdate((int)(100 * ++counter / calcItems), $"Warning! VMAT/MRLevelAtRestart option not found for {optimizationModel}");
                }
                else ProvideUIUpdate((int)(100 * ++counter / calcItems), $"MR restart level set to {MRrestart}");

                //set the GPU optimization option
                if (useGPUoptimization == "Yes")
                {
                    thePlan.SetCalculationOption(optimizationModel, "General/OptimizerSettings/UseGPU", useGPUoptimization);
                    ProvideUIUpdate((int)(100 * ++counter / calcItems), $"Set GPU option for optimization to {useGPUoptimization}");
                }
                else
                {
                    thePlan.SetCalculationOption(optimizationModel, "General/OptimizerSettings/UseGPU", "No");
                    ProvideUIUpdate((int)(100 * ++counter / calcItems), "Set GPU option for optimization to No");
                }

                //reference point can only be added for a plan that IS CURRENTLY OPEN
                //plan.AddReferencePoint(selectedSS.Structures.First(x => x.Id == "TS_PTV_VMAT"), null, "VMAT TBI", "VMAT TBI");

                //these need to be fixed
                //v16 of Eclipse allows for the creation of a plan with a named target structure and named primary reference point. Neither of these options are available in v15
                //plan.TargetVolumeID = selectedSS.Structures.First(x => x.Id == "xx");
                //plan.PrimaryReferencePoint = plan.ReferencePoints.Fisrt(x => x.Id == "xx");
                vmatPlans.Add(thePlan);
                ProvideUIUpdate((int)(100 * ++counter / calcItems), $"Added plan {itr.Item1} to stack!");
            }
            ProvideUIUpdate(100, "Finished creating and initializing plans!");
            ProvideUIUpdate($"Elapsed time: {GetElapsedTime()}");
            return false;
        }

        protected VVector RoundIsocenterPositions(VVector v, ExternalPlanSetup plan, ref int counter, ref int calcItems)
        {
            ProvideUIUpdate((int)(100 * ++counter / calcItems), "Rounding Y- and Z-positions to nearest integer values");
            //round z position to the nearest integer
            v = selectedSS.Image.DicomToUser(v, plan);
            v.x = Math.Round(v.x / 10.0f) * 10.0f;
            v.y = Math.Round(v.y / 10.0f) * 10.0f;
            v.z = Math.Round(v.z / 10.0f) * 10.0f;
            ProvideUIUpdate((int)(100 * ++counter / calcItems), $"Calculated isocenter position (user coordinates): ({v.x}, {v.y}, {v.z})");
            v = selectedSS.Image.UserToDicom(v, plan);
            ProvideUIUpdate((int)(100 * ++counter / calcItems), "Adding calculated isocenter position to stack!");
            return v;
        }

        //function used to contour the overlap between fields in adjacent isocenters for the VMAT Plans ONLY!
        //this option is requested by the user by selecting the checkbox on the main UI on the beam placement tab
        protected bool ContourFieldOverlap(Tuple<ExternalPlanSetup, List<Tuple<VVector, string, int>>> isoLocations, int isoCount)
        {
            UpdateUILabel("Contour field overlap:");

            ProvideUIUpdate($"Contour overlap margin: {contourOverlapMargin:0.0} cm");
            contourOverlapMargin *= 10;
            ProvideUIUpdate($"Contour overlap margin: {contourOverlapMargin:0.00} mm");

            int percentCompletion = 0;
            int calcItems = 3 + 7 * isoLocations.Item2.Count - 1;
            //grab target Id for this prescription item
            if(prescriptions.FirstOrDefault(y => string.Equals(y.Item1, isoLocations.Item1.Id)) == null)
            {
                ProvideUIUpdate($"Error! No matching prescrition found for iso plan name {isoLocations.Item1.Id}", true);
                return true;
            }
            string targetId = prescriptions.FirstOrDefault(y => string.Equals(y.Item1, isoLocations.Item1.Id)).Item2;
            Structure target_tmp = selectedSS.Structures.FirstOrDefault(x => string.Equals(x.Id, targetId));
            if (target_tmp == null)
            {
                ProvideUIUpdate($"Error getting target structure ({targetId}) for plan: {prescriptions.FirstOrDefault(y => string.Equals(y.Item1, isoLocations.Item1.Id))}! Exiting!", true);
                return true;
            }
            ProvideUIUpdate((int)(100 * ++percentCompletion / calcItems), $"Retrieved target: {target_tmp.Id} for plan: {isoLocations.Item1.Id}");
            //grab the image and get the z resolution and dicom origin (we only care about the z position of the dicom origin)
            Image image = selectedSS.Image;
            double zResolution = image.ZRes;
            VVector dicomOrigin = image.Origin;
            ProvideUIUpdate($"Retrived image: {image.Id}");
            ProvideUIUpdate($"Z resolution: {zResolution} mm");
            ProvideUIUpdate($"DICOM origin: ({dicomOrigin.x}, {dicomOrigin.y}, {dicomOrigin.z}) mm");

            //center position between adjacent isocenters, number of image slices to contour on, start image slice location for contouring
            List<Tuple<double, int, int>> overlap = new List<Tuple<double, int, int>> { };
            //calculate the center position between adjacent isocenters, number of image slices to contour on based on overlap and with additional user-specified margin (from main UI)
            //and the slice where the contouring should begin
            List<Structure> tmpJnxList = new List<Structure> { };
            for (int i = 1; i < isoLocations.Item2.Count; i++)
            {
                ProvideUIUpdate($"Junction: {i}");
                //this is left as a double so I can cast it to an int in the second overlap item and use it in the calculation in the third overlap item
                //logic to consider the situation where the y extent of the fields are NOT 40 cm!
                Beam iso1Beam1 = isoLocations.Item1.Beams.First(x => CalculationHelper.AreEqual(x.IsocenterPosition.z, isoLocations.Item2.ElementAt(i - 1).Item1.z));
                ProvideUIUpdate((int)(100 * ++percentCompletion / calcItems), $"First beam in isocenter {i - 1}: {iso1Beam1.Id}");

                Beam iso2Beam1 = isoLocations.Item1.Beams.First(x => CalculationHelper.AreEqual(x.IsocenterPosition.z, isoLocations.Item2.ElementAt(i).Item1.z));
                ProvideUIUpdate((int)(100 * ++percentCompletion / calcItems), $"First beam in isocenter {i}: {iso2Beam1.Id}");

                //assumes iso1beam1 y1 is oriented inferior on patient and iso2beam1 is oriented superior on patient
                double fieldLength = Math.Abs(iso1Beam1.GetEditableParameters().ControlPoints.First().JawPositions.Y1) + Math.Abs(iso2Beam1.GetEditableParameters().ControlPoints.First().JawPositions.Y2);
                ProvideUIUpdate((int)(100 * ++percentCompletion / calcItems), $"Field length ({iso1Beam1.Id} Y1 + {iso2Beam1.Id} Y2): {fieldLength} mm");

                double numSlices = Math.Ceiling(fieldLength + contourOverlapMargin - Math.Abs(isoLocations.Item2.ElementAt(i).Item1.z - isoLocations.Item2.ElementAt(i - 1).Item1.z));
                if(numSlices <= 0)
                {
                    ProvideUIUpdate($"Error! Calculated number of slices is <= 0 ({numSlices}) for junction: {i}!", true);
                    ProvideUIUpdate($"Field length: {fieldLength:0.00} mm");
                    ProvideUIUpdate($"Contour overlap margin: {contourOverlapMargin:0.00} mm");
                    ProvideUIUpdate($"Isocenter separation: {Math.Abs(isoLocations.Item2.ElementAt(i).Item1.z - isoLocations.Item2.ElementAt(i - 1).Item1.z):0.00}!");
                    return true;
                }
                ProvideUIUpdate((int)(100 * ++percentCompletion / calcItems), $"Number of slices to contour: {(int)(numSlices / zResolution)}");

                //calculate the center position between adjacent isocenters. NOTE: this calculation works from superior to inferior!
                double overlapCenter = isoLocations.Item2.ElementAt(i - 1).Item1.z + iso1Beam1.GetEditableParameters().ControlPoints.First().JawPositions.Y1  - contourOverlapMargin / 2 + numSlices / 2;
                ProvideUIUpdate((int)(100 * ++percentCompletion / calcItems), $"Overlap center position: {overlapCenter:0.00} mm");

                overlap.Add(new Tuple<double, int, int>(overlapCenter, // the center location
                                                        (int)(numSlices / zResolution), //total number of slices to contour
                                                        (int)((overlapCenter - numSlices / 2 - dicomOrigin.z) / zResolution))); // starting slice to contour
                ProvideUIUpdate((int)(100 * ++percentCompletion / calcItems), $"Starting slice to contour: {(int)((overlapCenter - numSlices / 2 - dicomOrigin.z) / zResolution)}");
                //add a new junction structure (named TS_jnx<i>) to the stack. Contours will be added to these structure later
                tmpJnxList.Add(selectedSS.AddStructure("CONTROL", $"TS_jnx{isoCount + i}"));
                ProvideUIUpdate((int)(100 * ++percentCompletion / calcItems), $"Added TS junction to stack: TS_jnx{isoCount + 1}");
            }
            jnxs.Add(Tuple.Create(isoLocations.Item1, tmpJnxList));

            //make a box at the min/max x,y positions of the target structure with no margin
            VVector[] targetBoundingBox = CreateTargetBoundingBox(target_tmp, 0.0);
            ProvideUIUpdate((int)(100 * ++percentCompletion / calcItems), $"Created target bounding box for contouring overlap");
           
            //add the contours to each relevant plan for each structure in the jnxs stack
            int count = 0;
            foreach (Tuple<double, int, int> value in overlap)
            {
                percentCompletion = 0;
                calcItems = value.Item2;
                ProvideUIUpdate(0, $"Contouring junction: {tmpJnxList.ElementAt(count).Id}");
                for (int i = value.Item3; i < (value.Item3 + value.Item2); i++)
                {
                    tmpJnxList.ElementAt(count).AddContourOnImagePlane(targetBoundingBox, i);
                    ProvideUIUpdate((int)(100 * ++percentCompletion / calcItems));
                }
                //only keep the portion of the box contour that overlaps with the target
                tmpJnxList.ElementAt(count).SegmentVolume = tmpJnxList.ElementAt(count).And(target_tmp.Margin(0));
                count++;
            }
            ProvideUIUpdate($"Elapsed time: {GetElapsedTime()}");
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
