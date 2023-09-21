﻿using System;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using VMATTBICSIAutoPlanningHelpers.BaseClasses;
using VMATTBICSIAutoPlanningHelpers.Prompts;
using VMATTBICSIAutoPlanningHelpers.Helpers;
using System.Runtime.ExceptionServices;

namespace VMATTBIEQAutoPlanMT.VMAT_TBI
{
    public class PlaceBeams_TBI : PlaceBeamsBase
    {
        //get methods
        public bool GetCheckIsoPlacementStatus() { return checkIsoPlacement; }
        public double GetCheckIsoPlacementLimit() { return checkIsoPlacementLimit; }

        //list plan, list<iso name, num beams for iso>
        private List<Tuple<string, List<Tuple<string, int>>>> planIsoBeamInfo;
        private ExternalPlanSetup vmatPlan = null;
        private ExternalPlanSetup legsPlan = null;

        //data members
        private double[] collRot;
        private double gantryStart;
        private double gantryStop;
        private ExternalBeamMachineParameters ebmpArc;
        private ExternalBeamMachineParameters ebmpStatic;
        private List<VRect<double>> jawPos;
        private double targetMargin;
        private int numVMATIsos;
        private int totalNumIsos;
        private int totalNumVMATBeams;
        protected double checkIsoPlacementLimit = 5.0;
        protected bool checkIsoPlacement = false;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="ss"></param>
        /// <param name="planInfo"></param>
        /// <param name="coll"></param>
        /// <param name="jp"></param>
        /// <param name="linac"></param>
        /// <param name="energy"></param>
        /// <param name="calcModel"></param>
        /// <param name="optModel"></param>
        /// <param name="gpuDose"></param>
        /// <param name="gpuOpt"></param>
        /// <param name="mr"></param>
        /// <param name="tgtMargin"></param>
        /// <param name="overlap"></param>
        /// <param name="overlapMargin"></param>
        /// <param name="closePW"></param>
        public PlaceBeams_TBI(StructureSet ss, 
                              List<Tuple<string, List<Tuple<string, int>>>> planInfo, 
                              double[] coll, 
                              List<VRect<double>> jp, 
                              string linac, 
                              string energy, 
                              string calcModel, 
                              string optModel, 
                              string gpuDose, 
                              string gpuOpt, 
                              string mr, 
                              double tgtMargin, 
                              bool overlap, 
                              double overlapMargin, 
                              bool closePW)
        {
            selectedSS = ss;
            planIsoBeamInfo = new List<Tuple<string, List<Tuple<string, int>>>>(planInfo);
            numVMATIsos = planIsoBeamInfo.First().Item2.Count;
            if (planIsoBeamInfo.Count > 1) totalNumIsos = numVMATIsos + planIsoBeamInfo.Last().Item2.Count;
            else totalNumIsos = numVMATIsos;
            collRot = coll;
            jawPos = new List<VRect<double>>(jp);
            ebmpArc = new ExternalBeamMachineParameters(linac, energy, 600, "ARC", null);
            //AP/PA beams always use 6X
            ebmpStatic = new ExternalBeamMachineParameters(linac, "6X", 600, "STATIC", null);
            //copy the calculation model
            calculationModel = calcModel;
            optimizationModel = optModel;
            useGPUdose = gpuDose;
            useGPUoptimization = gpuOpt;
            MRrestart = mr;
            //convert from cm to mm
            targetMargin = tgtMargin * 10.0;
            //user wants to contour the overlap between fields in adjacent VMAT isocenters
            contourOverlap = overlap;
            contourOverlapMargin = overlapMargin;
            SetCloseOnFinish(closePW, 3000);
        }
        
        /// <summary>
        /// Run control
        /// </summary>
        /// <returns></returns>
        [HandleProcessCorruptedStateExceptions]
        public override bool Run()
        {
            try
            {
                if (CheckExistingCourse()) return true;
                if (CheckExistingPlans()) return true;
                if (CreateVMATPlans()) return true;
                vmatPlan = vmatPlans.First();
                if (planIsoBeamInfo.Count > 1 && CreateAPPAPlan()) return true;
                //plan, List<isocenter position, isocenter name, number of beams per isocenter>
                List<Tuple<ExternalPlanSetup, List<Tuple<VVector, string, int>>>> isoLocations = GetIsocenterPositions();
                UpdateUILabel("Assigning isocenters and beams: ");
                if (SetVMATBeams(isoLocations.First())) return true;
                //ensure contour overlap is requested AND there are more than two isocenters for this plan
                if (contourOverlap && isoLocations.First().Item2.Count > 1) if (ContourFieldOverlap(isoLocations.First(), 0)) return true;
                if (isoLocations.Count > 1)
                {
                    if (SetAPPABeams(isoLocations.Last())) return true;
                }
                UpdateUILabel("Finished!");
                return false;
            }
            catch (Exception e)
            {
                ProvideUIUpdate($"{e.Message}", true);
                stackTraceError = e.StackTrace;
                return true;
            }
        }

        /// <summary>
        /// Overridden method to check for existing plans in the course. Builds on the method in the base class, but adds additional checks for the legs
        /// AP/PA plan
        /// </summary>
        /// <returns></returns>
        protected override bool CheckExistingPlans()
        {
            //check for vmat plans (contained in prescriptions vector)
            if (base.CheckExistingPlans()) return true;

            //check for any plans containing 'legs' if the total number of isocenters is greater than the number of vmat isocenters
            if (planIsoBeamInfo.Count > 1 && theCourse.ExternalPlanSetups.Any(x => x.Id.ToLower().Contains("legs")))
            {
                ProvideUIUpdate(0, $"One or more legs plans exist in course {theCourse.Id}");
                ProvideUIUpdate("ESAPI can't remove plans in the clinical environment!");
                ProvideUIUpdate("Please manually remove this plan and try again.", true);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Helper method to create the legs AP/PA plan
        /// </summary>
        /// <returns></returns>
        private bool CreateAPPAPlan()
        {
            UpdateUILabel("Creating AP/PA plan: ");
            int percentComplete = 0;
            int calcItems = 4;
            legsPlan = theCourse.AddExternalPlanSetup(selectedSS);
            ProvideUIUpdate(100 * ++percentComplete / calcItems, $"Creating AP/PA plan");

            legsPlan.Id = "_Legs";
            ProvideUIUpdate(100 * ++percentComplete / calcItems, $"Set plan Id for {legsPlan.Id}");

            //grab the highest Rx prescription for this plan (should only be one entry since no boost plans are permitted for TBI)
            List<Tuple<string, string, int, DoseValue, double>> rx = TargetsHelper.GetHighestRxPrescriptionForEachPlan(prescriptions);
            //100% dose prescribed in plan
            legsPlan.SetPrescription(rx.First().Item3, rx.First().Item4, 1.0);
            ProvideUIUpdate(100 * ++percentComplete / calcItems, $"Set prescription for plan {legsPlan.Id}");
            legsPlan.SetCalculationModel(CalculationType.PhotonVolumeDose, calculationModel);
            ProvideUIUpdate(100 * ++percentComplete / calcItems, $"Set calculation model to {calculationModel}");
            return false;
        }

        /// <summary>
        /// Helper method to calculate the optimal isocenter positions for the VMAT plan
        /// </summary>
        /// <param name="targetSupExtent"></param>
        /// <param name="targetInfExtent"></param>
        /// <param name="supInfTargetMargin"></param>
        /// <param name="maxFieldYExtent"></param>
        /// <param name="minOverlap"></param>
        /// <returns></returns>
        private List<Tuple<VVector, string, int>> CalculateVMATIsoPositions(double targetSupExtent, 
                                                                            double targetInfExtent, 
                                                                            double supInfTargetMargin, 
                                                                            double maxFieldYExtent, 
                                                                            double minOverlap)
        {
            int percentComplete = 0;
            int calcItems = 10;
            List<Tuple<VVector, string, int>> tmp = new List<Tuple<VVector, string, int>> { };
            Image _image = selectedSS.Image;
            VVector userOrigin = _image.UserOrigin;
            double isoSeparation = CalculateIsocenterSeparation(targetSupExtent, targetInfExtent, maxFieldYExtent, minOverlap, numVMATIsos);

            for (int i = 0; i < numVMATIsos; i++)
            {
                VVector v = new VVector();
                v.x = userOrigin.x;
                v.y = userOrigin.y;
                //6-10-2020 EAS, want to count up from matchplane to ensure distance from matchplane is fixed at 190 mm
                v.z = targetInfExtent + (numVMATIsos - i - 1) * isoSeparation + (maxFieldYExtent / 2 - supInfTargetMargin);
                //round z position to the nearest integer
                v = RoundIsocenterPositions(v, vmatPlan);
                ProvideUIUpdate(100 * ++percentComplete / calcItems, $"Calculated isocenter position {i + 1}");
                tmp.Add(new Tuple<VVector, string, int>(RoundIsocenterPositions(v, vmatPlan),
                                                        planIsoBeamInfo.First().Item2.ElementAt(i).Item1,
                                                        planIsoBeamInfo.First().Item2.ElementAt(i).Item2));
            }

            return tmp;
        }

        /// <summary>
        /// Helper method to calculate the optimal isocenter positions for the AP/PA legs plan
        /// </summary>
        /// <param name="targetSupExtent"></param>
        /// <param name="targetInfExtent"></param>
        /// <param name="maxFieldYExtent"></param>
        /// <param name="minOverlap"></param>
        /// <param name="lastVMATIsoZPosition"></param>
        /// <returns></returns>
        private List<Tuple<VVector, string, int>> CalculateAPPAIsoPositions(double targetSupExtent, 
                                                                            double targetInfExtent, 
                                                                            double maxFieldYExtent, 
                                                                            double minOverlap, 
                                                                            double lastVMATIsoZPosition)
        {
            int percentComplete = 0;
            int calcItems = 10;
            List<Tuple<VVector, string, int>> tmp = new List<Tuple<VVector, string, int>> { };
            Image _image = selectedSS.Image;
            VVector userOrigin = _image.UserOrigin;
            double isoSeparation = CalculateIsocenterSeparation(targetSupExtent, targetInfExtent, maxFieldYExtent, minOverlap, totalNumIsos - numVMATIsos);

            double offset = lastVMATIsoZPosition - targetSupExtent;
            for (int i = 0; i < (totalNumIsos - numVMATIsos); i++)
            {
                VVector v = new VVector();
                v.x = userOrigin.x;
                v.y = userOrigin.y;
                //5-11-2020 update EAS (the first isocenter immediately inferior to the matchline is now a distance = offset away). This ensures the isocenters immediately inferior and superior to the 
                //matchline are equidistant from the matchline
                v.z = targetSupExtent - i * isoSeparation - offset;

                v = RoundIsocenterPositions(v, legsPlan);
                ProvideUIUpdate(100 * ++percentComplete / calcItems, $"Calculated isocenter position {i + 1}");
                tmp.Add(new Tuple<VVector, string, int>(RoundIsocenterPositions(v, legsPlan),
                                                        planIsoBeamInfo.Last().Item2.ElementAt(i).Item1,
                                                        planIsoBeamInfo.Last().Item2.ElementAt(i).Item2));
            }
            return tmp;
        }

        /// <summary>
        /// Utility method to calculate the optimal separation between isocenters (for both VMAT and AP/PA)
        /// </summary>
        /// <param name="targetSupExtent"></param>
        /// <param name="targetInfExtent"></param>
        /// <param name="maxFieldYExtent"></param>
        /// <param name="minOverlap"></param>
        /// <param name="numIso"></param>
        /// <returns></returns>
        private double CalculateIsocenterSeparation(double targetSupExtent,
                                                    double targetInfExtent,
                                                    double maxFieldYExtent,
                                                    double minOverlap,
                                                    int numIso)
        {
            //to ensure we won't divide by 0 for the AP/PA plan if there is only one AP/PA isocenter
            if (numIso < 2) numIso = 2;
            double isoSeparation = Math.Round(((targetSupExtent - targetInfExtent - (maxFieldYExtent - minOverlap)) / (numIso - 1)) / 10.0f) * 10.0f;
            if (isoSeparation > (maxFieldYExtent - minOverlap))
            {
                ConfirmPrompt CP = new ConfirmPrompt("Calculated isocenter separation > 38.0 cm, which reduces the overlap between adjacent fields!" + Environment.NewLine + Environment.NewLine + "Truncate isocenter separation to 38.0 cm?!");
                CP.ShowDialog();
                if (CP.GetSelection())
                {
                    isoSeparation = maxFieldYExtent - minOverlap;
                }
            }
            return isoSeparation;
        }

        /// <summary>
        /// Utility method to calculate the isocenter positions for all plans
        /// </summary>
        /// <returns></returns>
        protected override List<Tuple<ExternalPlanSetup, List<Tuple<VVector, string, int>>>> GetIsocenterPositions()
        {
            List<Tuple<ExternalPlanSetup, List<Tuple<VVector, string, int>>>> allIsocenters = new List<Tuple<ExternalPlanSetup, List<Tuple<VVector, string, int>>>> { };
            List<Tuple<VVector, string, int>> tmp = new List<Tuple<VVector, string, int>> { };

            Image image = selectedSS.Image;
            VVector userOrigin = image.UserOrigin;
            //manually calculate the target sup/inf extent to avoid having to figure out if flash was used or not
            Structure target = StructureTuningHelper.GetStructureFromId("body", selectedSS);
            double targetSupExtent = target.MeshGeometry.Positions.Max(p => p.Z) - targetMargin;
            double targetInfExtent = target.MeshGeometry.Positions.Min(p => p.Z) + targetMargin;

            //matchline is present and not empty
            if (StructureTuningHelper.DoesStructureExistInSS("matchline",selectedSS,true))
            {
                Structure matchline = StructureTuningHelper.GetStructureFromId("matchline", selectedSS);
                tmp = CalculateVMATIsoPositions(targetSupExtent, matchline.CenterPoint.z, 10.0, 400.0, 20.0);
                allIsocenters.Add(Tuple.Create(vmatPlan, new List<Tuple<VVector, string, int>>(tmp)));
                allIsocenters.Add(Tuple.Create(legsPlan, new List<Tuple<VVector, string, int>>(CalculateAPPAIsoPositions(matchline.CenterPoint.z, 
                                                                                                                         targetInfExtent, 
                                                                                                                         400.0, 
                                                                                                                         20.0, 
                                                                                                                         tmp.Last().Item1.z))));
            }
            else
            {
                tmp = CalculateVMATIsoPositions(targetSupExtent, targetInfExtent, 10.0, 400.0, 20.0);
                allIsocenters.Add(Tuple.Create(vmatPlan, new List<Tuple<VVector, string, int>>(tmp)));
            }

            VVector firstIso = allIsocenters.First().Item2.First().Item1;
            VVector lastIso = allIsocenters.Last().Item2.Last().Item1;
            //if the most superior isocenter + 20 cm - most superior extent of target is < limit or
            //if the most inferior target extent - (most superior isocenter position - 20 cm) < limit, notify the user that the target may not be fully covered
            if ((firstIso.z + 200.0 - targetSupExtent < checkIsoPlacementLimit) || 
                (targetInfExtent - (lastIso.z - 200.0) < checkIsoPlacementLimit)) checkIsoPlacement = true;

            return allIsocenters;
        }

        /// <summary>
        /// Helper method to place the VMAT beams at the calculated optimization isocenter positions
        /// </summary>
        /// <param name="iso"></param>
        /// <returns></returns>
        protected override bool SetVMATBeams(Tuple<ExternalPlanSetup, List<Tuple<VVector, string, int>>> iso)
        {
            ProvideUIUpdate(0, $"Preparing to set isocenters for plan: {iso.Item1.Id}");
            int percentComplete = 0;
            int calcItems = 1;
            //DRR parameters (dummy parameters to generate DRRs for each field)
            DRRCalculationParameters DRR = GenerateDRRParameters();
            ProvideUIUpdate(100 * ++percentComplete / calcItems, "Created default DRR parameters");

            //place the beams for the VMAT plan
            //unfortunately, all of Nataliya's requirements for beam placement meant that this process couldn't simply draw from beam placement templates. Some of the beam placements for specific isocenters
            //and under certain conditions needed to be hard-coded into the script. I'm not really a fan of this, but it was the only way to satisify Nataliya's requirements.
            ProvideUIUpdate(100, "Preparation complete!");

            //place the beams for the VMAT plan
            VRect<double> jp;
            calcItems = 0;
            percentComplete = 0;
            for (int i = 0; i < iso.Item2.Count; i++)
            {
                calcItems += iso.Item2.ElementAt(i).Item3 * 5;
                ProvideUIUpdate(0, $"Assigning isocenter: {i + 1}");
                //beam counter
                for (int j = 0; j < iso.Item2.ElementAt(i).Item3; j++)
                {
                    //second isocenter and third beam requires the x-jaw positions to be mirrored about the y-axis (these jaw positions are in the fourth element of the jawPos list)
                    //this is generally the isocenter located in the pelvis and we want the beam aimed at the kidneys-area
                    if (i == 1 && j == 2) jp = jawPos.ElementAt(j + 1);
                    else if (i == 1 && j == 3) jp = jawPos.ElementAt(j - 1);
                    else jp = jawPos.ElementAt(j);
                    ;
                    
                    double coll = collRot[j];
                    if ((totalNumIsos > numVMATIsos) && (i == (numVMATIsos - 1)))
                    {
                        //zero collimator rotations of two main fields for beams in isocenter immediately superior to matchline. 
                        //Adjust the third beam such that collimator rotation is 90 degrees. Do not adjust 4th beam
                        if (j < 2) coll = 0.0;
                        else if (j == 2) coll = 90.0;
                    }

                    //all even beams (e.g., 2, 4, etc.) will be CCW and all odd beams will be CW
                    GantryDirection direction;
                    if (totalNumVMATBeams % 2 == 0)
                    {
                        direction = GantryDirection.CounterClockwise;
                        gantryStart = 179.0;
                        gantryStop = 181.0;
                    }
                    else
                    {
                        direction = GantryDirection.Clockwise;
                        gantryStart = 181.0;
                        gantryStop = 179.0;
                    }

                    Beam b = vmatPlan.AddArcBeam(ebmpArc, jp, coll, gantryStart, gantryStop, direction, 0, iso.Item2.ElementAt(i).Item1);
                    ProvideUIUpdate(100 * ++percentComplete / calcItems, $"Added arc beam to iso: {i + 1}");

                    //id = <beam num> <rotation direction> <iso name><coll rotation>
                    b.Id = $"{totalNumVMATBeams + 1} {(direction == GantryDirection.CounterClockwise ? "CCW" : "CW")} {iso.Item2.ElementAt(i).Item2}{(j > 1 ? "90" : "")}";
                    ProvideUIUpdate(100 * ++percentComplete / calcItems, $"Assigned beam id: {b.Id}");

                    b.CreateOrReplaceDRR(DRR);
                    ProvideUIUpdate(100 * ++percentComplete / calcItems, $"Assigned DRR to beam: {b.Id}");

                    totalNumVMATBeams++;
                }
            }
            ProvideUIUpdate($"Elapsed time: {GetElapsedTime()}");
            return false;
        }

        /// <summary>
        /// Method to create and place the AP/PA fields in the legs plan
        /// </summary>
        /// <param name="iso"></param>
        /// <returns></returns>
        private bool SetAPPABeams(Tuple<ExternalPlanSetup, List<Tuple<VVector, string, int>>> iso)
        {
            ProvideUIUpdate(0, $"Preparing to set isocenters for plan: {iso.Item1.Id}");
            int percentComplete = 0;
            int calcItems = 3 + iso.Item2.First().Item3 * 5;

            Structure target = StructureTuningHelper.GetStructureFromId("body", selectedSS);
            ProvideUIUpdate(100 * ++percentComplete / calcItems, "Retrieved body structure");
            double targetInfExtent = target.MeshGeometry.Positions.Min(p => p.Z) + targetMargin;
            ProvideUIUpdate(100 * ++percentComplete / calcItems, $"Calculated target inferior extent: {targetInfExtent}");

            ProvideUIUpdate("Preparation complete!");

            //place the beams for the VMAT plan
            int count = totalNumVMATBeams;
            ProvideUIUpdate($"Assigning isocenter: {1}");

            double x1, y1, x2, y2;
            x1 = y1 = -200.0;
            y2 = 200;
            //adjust x2 jaw (furthest from matchline) so that it covers edge of target volume
            x2 = CalculateX2JawPosition(iso.Item2.First().Item1.z, targetInfExtent, 20.0);
            VRect<double> jaws = GenerateJawsPositions(x1, y1, x2, y2, iso.Item2.First().Item2);
            ProvideUIUpdate(100 * ++percentComplete / calcItems);

            //AP field
            float[,] MLCpos = BuildMLCArray(x1, x2);
            ProvideUIUpdate(100 * ++percentComplete / calcItems, $"Generated MLC positions for iso: {iso.Item2.First().Item2}");
            
            CreateStaticBeam(++count, "Upper", 0.0, MLCpos, jaws, iso.Item2.First().Item1);
            ProvideUIUpdate(100 * ++percentComplete / calcItems, $"Added AP beam to iso: {iso.Item2.First().Item2}");

            //PA field
            CreateStaticBeam(++count, "Upper", 180.0, MLCpos, jaws, iso.Item2.First().Item1);
            ProvideUIUpdate(100 * ++percentComplete / calcItems, $"Added PA beam to iso: {iso.Item2.First().Item2}");

            //lower legs field if applicable
            if (iso.Item2.Count > 1)
            {
                ProvideUIUpdate($"Added beams to lower leg isocenter: {iso.Item2.Last().Item2}");
                calcItems += iso.Item2.Last().Item3 * 5;
                VVector infIso = new VVector
                {
                    //the element at numVMATIsos in isoLocations vector is the first AP/PA isocenter
                    x = iso.Item2.Last().Item1.x,
                    y = iso.Item2.Last().Item1.y
                };

                //if the distance between the matchline and the inferior edge of the target is < 600 mm, set the beams in the second isocenter (inferior-most) to be half-beam blocks
                double legsTargetExtent = StructureTuningHelper.GetStructureFromId("matchline", selectedSS).CenterPoint.z - targetInfExtent;
                if (legsTargetExtent < 600.0)
                {
                    ProvideUIUpdate($"Separation between matchline center z and target inferior extent: {legsTargetExtent:0.0} mm");
                    infIso.z = iso.Item2.First().Item1.z - 200.0;
                    ProvideUIUpdate($"legs target extent is < 60 cm! Adjusting isocenter z position from {iso.Item2.First().Item1.z:0.0} mm to {infIso.z:0.0} mm");
                    ProvideUIUpdate($"Setting X1 jaw position to 0.0 --> Half beam block");
                    x1 = 0.0;
                }
                else infIso.z = iso.Item2.First().Item1.z - 390.0;
                ProvideUIUpdate(100 * ++percentComplete / calcItems, $"Calculated lower leg isocenter: ({infIso.x:0.0}, {infIso.y:0.0}, {infIso.z:0.0}) mm");

                //fit x1 jaw to extent of patient
                x2 = CalculateX2JawPosition(infIso.z, targetInfExtent, 20.0);
                jaws = GenerateJawsPositions(x1, y1, x2, y2, iso.Item2.Last().Item2);
                ProvideUIUpdate(100 * ++percentComplete / calcItems);

                //set MLC positions
                MLCpos = BuildMLCArray(x1, x2);
                ProvideUIUpdate(100 * ++percentComplete / calcItems, $"Generated MLC positions for iso: {iso.Item2.Last().Item2}");
                
                //AP field
                CreateStaticBeam(++count, "Lower", 0.0, MLCpos, jaws, infIso);
                ProvideUIUpdate(100 * ++percentComplete / calcItems, $"Added AP beam to iso: {iso.Item2.Last().Item2}");

                //PA field
                CreateStaticBeam(++count, "Lower", 180.0, MLCpos, jaws, infIso);
                ProvideUIUpdate(100 * ++percentComplete / calcItems, $"Added PA beam to iso: {iso.Item2.Last().Item2}");
            }
            ProvideUIUpdate($"Elapsed time: {GetElapsedTime()}");
            return false;
        }

        /// <summary>
        /// Simple utility method to add a static MLC beam to the AP/PA plan, add a DRR, and assign the beam Id
        /// </summary>
        /// <param name="beamCount"></param>
        /// <param name="beamPositionId"></param>
        /// <param name="gantryAngle"></param>
        /// <param name="MLCs"></param>
        /// <param name="jaws"></param>
        /// <param name="iso"></param>
        private void CreateStaticBeam(int beamCount, string beamPositionId, double gantryAngle, float[,] MLCs, VRect<double> jaws, VVector iso)
        {
            Beam b = legsPlan.AddMLCBeam(ebmpStatic, MLCs, jaws, 90.0, gantryAngle, 0.0, iso);
            b.CreateOrReplaceDRR(GenerateDRRParameters());
            b.Id = $"{beamCount} {(CalculationHelper.AreEqual(gantryAngle, 0.0) ? "AP" : "PA")} {beamPositionId} Legs";
        }

        /// <summary>
        /// Simple helper method to take the calculated jaw positions and convert them to a VRect. Also print the jaw positions once converted
        /// </summary>
        /// <param name="x1"></param>
        /// <param name="y1"></param>
        /// <param name="x2"></param>
        /// <param name="y2"></param>
        /// <param name="isoId"></param>
        /// <returns></returns>
        private VRect<double> GenerateJawsPositions(double x1, double y1, double x2, double y2, string isoId)
        {
            VRect<double> jaws = new VRect<double>(x1, y1, x2, y2);
            ProvideUIUpdate($"Calculated jaw positions for isocenter: {isoId}");
            ProvideUIUpdate($"x1: {jaws.X1:0.0}");
            ProvideUIUpdate($"x2: {jaws.X2:0.0}");
            ProvideUIUpdate($"y1: {jaws.Y1:0.0}");
            ProvideUIUpdate($"y2: {jaws.Y2:0.0}");
            return jaws;
        }

        /// <summary>
        /// Simple utility method to calculate the necessary position of the X2 jaw to cover the inf extent of the target
        /// </summary>
        /// <param name="isoZPosition"></param>
        /// <param name="targetInfExtent"></param>
        /// <param name="minFieldOverlap"></param>
        /// <returns></returns>
        private double CalculateX2JawPosition(double isoZPosition, double targetInfExtent, double minFieldOverlap)
        {
            //adjust x2 jaw (furthest from matchline) so that it covers inf edge of target volume
            double x2 = isoZPosition - (targetInfExtent - minFieldOverlap);
            if (x2 > 200.0) x2 = 200.0;
            else if (x2 < 10.0) x2 = 10.0;
            return x2;
        }

        /// <summary>
        /// Simple helper method to build the position 2D array for the MLCs
        /// </summary>
        /// <param name="x1"></param>
        /// <param name="x2"></param>
        /// <returns></returns>
        private float[,] BuildMLCArray(double x1, double x2)
        {
            //set MLC positions. First row is bank number 0 (X1 leaves) and second row is bank number 1 (X2).
            float[,] MLCpos = new float[2, 60];
            for (int j = 0; j < 60; j++)
            {
                MLCpos[0, j] = (float)(x1);
                MLCpos[1, j] = (float)(x2);
            }
            return MLCpos;
        }
    }
}
