﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.Windows.Media.Media3D;

namespace VMATAutoPlanMT
{
    public class generateTS_CSI : generateTSbase
    { 
        //structure, sparing type, added margin
        public List<Tuple<string, string, double>> spareStructList;
        //DICOM types
        //Possible values are "AVOIDANCE", "CAVITY", "CONTRAST_AGENT", "CTV", "EXTERNAL", "GTV", "IRRAD_VOLUME", 
        //"ORGAN", "PTV", "TREATED_VOLUME", "SUPPORT", "FIXATION", "CONTROL", and "DOSE_REGION". 
        List<Tuple<string, string>> TS_structures;
        public int numIsos;
        public int numVMATIsos;
        public bool updateSparingList = false;

        public generateTS_CSI(List<Tuple<string, string>> ts, List<Tuple<string, string, double>> list, StructureSet ss)
        {
            TS_structures = new List<Tuple<string, string>>(ts);
            spareStructList = new List<Tuple<string, string, double>>(list);
            selectedSS = ss;
        }

        public override bool generateStructures()
        {
            isoNames.Clear();
            if (preliminaryChecks()) return true;
            if (createTSStructures()) return true;
            if (calculateNumIsos()) return true;
            MessageBox.Show("Structures generated successfully!\nPlease proceed to the beam placement tab!");
            return false;
        }

        public override bool preliminaryChecks()
        {
            //check if user origin was set
            if (isUOriginInside()) return true;

            //verify brain and spine structures are present
            if(selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "brain") == null || selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "spinalcord") == null)
            {
                MessageBox.Show("Missing brain and/or spine structures! Please add and try again!");
                return true;
            }

            //check if selected structures are empty or of high-resolution (i.e., no operations can be performed on high-resolution structures)
            string output = "The following structures are high-resolution:" + System.Environment.NewLine;
            List<Structure> highResStructList = new List<Structure> { };
            List<Tuple<string, string, double>> highResSpareList = new List<Tuple<string, string, double>> { };
            foreach (Tuple<string, string, double> itr in spareStructList)
            {
                if (itr.Item2 == "Mean Dose < Rx Dose")
                {
                    if (selectedSS.Structures.First(x => x.Id == itr.Item1).IsEmpty)
                    {
                        MessageBox.Show(String.Format("Error! \nThe selected structure that will be subtracted from PTV_Body and TS_PTV_VMAT is empty! \nContour the structure and try again."));
                        return true;
                    }
                    else if (selectedSS.Structures.First(x => x.Id == itr.Item1).IsHighResolution)
                    {
                        highResStructList.Add(selectedSS.Structures.First(x => x.Id == itr.Item1));
                        highResSpareList.Add(itr);
                        output += String.Format("{0}", itr.Item1) + System.Environment.NewLine;
                    }
                }
            }
            //if there are high resolution structures, they will need to be converted to default resolution.
            if (highResStructList.Count() > 0)
            {
                //ask user if they are ok with converting the relevant high resolution structures to default resolution
                output += "They must be converted to default resolution before proceeding!";
                confirmUI CUI = new confirmUI();
                CUI.message.Text = output + Environment.NewLine + Environment.NewLine + "Continue?!";
                CUI.message.Font = new System.Drawing.Font("Microsoft Sans Serif", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
                CUI.ShowDialog();
                if (!CUI.confirm) return true;

                List<Tuple<string, string, double>> newData = convertHighToLowRes(highResStructList, highResSpareList, spareStructList);
                if (!newData.Any()) return true;
                spareStructList = new List<Tuple<string, string, double>>(newData);
                //inform the main UI class that the UI needs to be updated
                updateSparingList = true;
            }
            return false;
        }

        public bool calculateNumIsos()
        {
            //get the points collection for the Body (used for calculating number of isocenters)
            Point3DCollection pts = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "body").MeshGeometry.Positions;

            //For these cases the maximum number of allowed isocenters is 3.
            //the reason for the explicit statements calculating the number of isos and then truncating them to 3 was to account for patients requiring < 3 isos and if, later on, we want to remove the restriction of 3 isos
            numIsos = numVMATIsos = (int)Math.Ceiling(((pts.Max(p => p.Z) - pts.Min(p => p.Z)) / (400.0 - 20.0)));
            if (numIsos > 3) numIsos = numVMATIsos = 3;

            //set isocenter names based on numIsos and numVMATIsos (determined these names from prior cases)
            isoNames = new List<string>(new isoNameHelper().getIsoNames(numVMATIsos, numIsos));
            return false;
        }

        public bool createTargetStructures()
        {
            //create the CTV and PTV structures
            //if these structures were present, they should have been removed (regardless if they were contoured or not). 
            List<Structure> addedTargets = new List<Structure> { };
            foreach (Tuple<string, string> itr in TS_structures.Where(x => x.Item2.ToLower().Contains("ctv") || x.Item2.ToLower().Contains("ptv")).OrderBy(x => x.Item2))
            {
                if (selectedSS.CanAddStructure(itr.Item1, itr.Item2))
                {
                    addedStructures.Add(itr.Item2);
                    addedTargets.Add(selectedSS.AddStructure(itr.Item1, itr.Item2));
                }
                else
                {
                    MessageBox.Show(String.Format("Can't add {0} to the structure set!", itr.Item2));
                    return true;
                }
            }

            foreach(Structure itr in addedTargets)
            {
                if (itr.Id.ToLower().Contains("brain"))
                {
                    Structure tmp = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "brain");
                    if (tmp != null && !tmp.IsEmpty)
                    {
                        if (itr.Id.ToLower().Contains("ctv"))
                        {
                            //CTV structure. Brain CTV IS the brain structure
                            itr.SegmentVolume = tmp.Margin(0.0);
                        }
                        else
                        {
                            //PTV structure
                            //5 mm uniform margin to generate PTV
                            itr.SegmentVolume = tmp.Margin(5.0);
                        }
                    }
                    else { MessageBox.Show("Error! Could not retrieve brain structure! Exiting!"); return true; }
                }
                else if(itr.Id.ToLower().Contains("spine"))
                {
                    Structure tmp = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "spinalcord");
                    if (tmp != null && !tmp.IsEmpty)
                    {
                        if (itr.Id.ToLower().Contains("ctv"))
                        {
                            //CTV structure. Brain CTV IS the brain structure
                            //AxisAlignedMargins(inner or outer margin, margin from negative x, margin for negative y, margin for negative z, margin for positive x, margin for positive y, margin for positive z)
                            //according to Nataliya: CTV_spine = spinal_cord+0.5cm ANT, +1.5cm Inf, and +1.0 cm in all other directions
                            itr.SegmentVolume = tmp.AsymmetricMargin(new AxisAlignedMargins(StructureMarginGeometry.Outer,
                                                                                            10.0,
                                                                                            5.0,
                                                                                            15.0,
                                                                                            10.0,
                                                                                            10.0,
                                                                                            10.0));
                        }
                        else
                        {
                            //PTV structure
                            //5 mm uniform margin to generate PTV
                            tmp = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "ctv_spine");
                            if (tmp != null && !tmp.IsEmpty)
                            {
                                itr.SegmentVolume = tmp.Margin(5.0);
                            }
                            else { MessageBox.Show("Error! Could not retrieve CTV_Spine structure! Exiting!"); return true; }
                        }
                    }
                    else { MessageBox.Show("Error! Could not retrieve brain structure! Exiting!"); return true; }
                }
            }
                
            foreach(Structure itr in addedTargets.Where(x => x.Id.ToLower().Contains("csi")))
            {
                Structure tmp = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower().Contains("ptv_brain"));
                Structure tmp1 = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower().Contains("ptv_spine"));
                itr.SegmentVolume = tmp.Margin(0.0);
                itr.SegmentVolume = itr.Or(tmp1.Margin(0.0));
            }

            return false;
        }

        public override bool createTSStructures()
        {
            if (RemoveOldTSStructures(TS_structures)) return true;
            if (createTargetStructures()) return true;

            //determine if any TS structures need to be added to the selected structure set
            foreach (Tuple<string, string, double> itr in spareStructList)
            {
                optParameters.Add(Tuple.Create(itr.Item1, itr.Item2));
                if (itr.Item2 == "Mean Dose < Rx Dose") foreach (Tuple<string, string> itr1 in TS_structures.Where(x => x.Item2.ToLower().Contains(itr.Item1.ToLower()))) AddTSStructures(itr1);
            }

            //now contour the various structures
            foreach (string itr in addedStructures)
            {
                Structure tmp = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == itr.ToLower());
                //MessageBox.Show(s);
                if (!(itr.ToLower().Contains("ptv")))
                {
                    Structure tmp1 = null;
                    double margin = 0.0;
                    if (itr.ToLower().Contains("_low"))
                    {
                        if (selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower().Contains(itr.ToLower()) && x.Id.ToLower().Contains("_low")) == null) tmp1 = selectedSS.Structures.First(x => x.Id.ToLower().Contains(itr.ToLower()));
                        else tmp1 = selectedSS.Structures.First(x => x.Id.ToLower().Contains(itr.ToLower()) && x.Id.ToLower().Contains("_low"));
                    }
                    //all structures in TS_structures and scleroStructures are inner margins, which is why the below code works.
                    int pos1 = itr.IndexOf("-");
                    int pos2 = itr.IndexOf("cm");
                    if (pos1 != -1 && pos2 != -1) double.TryParse(itr.Substring(pos1, pos2 - pos1), out margin);

                    //convert from cm to mm
                    tmp.SegmentVolume = tmp1.Margin(margin * 10);
                }
                else if (itr.ToLower() == "ptv_body")
                {
                    //get the body contour and create the ptv structure using the user-specified inner margin
                    Structure tmp1 = selectedSS.Structures.First(x => x.Id.ToLower() == "body");
                   // tmp.SegmentVolume = tmp1.Margin(-targetMargin * 10);

                    //subtract all the structures the user wants to spare from PTV_Body
                    foreach (Tuple<string, string, double> spare in spareStructList)
                    {
                        if (spare.Item2 == "Mean Dose < Rx Dose")
                        {
                            
                            tmp1 = selectedSS.Structures.First(x => x.Id.ToLower() == spare.Item1.ToLower());
                            tmp.SegmentVolume = tmp.Sub(tmp1.Margin((spare.Item3) * 10));
                        }
                    }
                }
                else if (itr.ToLower() == "ts_ptv_vmat")
                {
                    //copy the ptv_body contour onto the TS_ptv_vmat contour
                    Structure tmp1 = selectedSS.Structures.First(x => x.Id.ToLower() == "ptv_body");
                    tmp.SegmentVolume = tmp1.Margin(0.0);

                    //matchplane exists and needs to be cut from TS_PTV_Body. Also remove all TS_PTV_Body segements inferior to match plane
                    if (selectedSS.Structures.Where(x => x.Id.ToLower() == "matchline").Any())
                    {
                        //find the image plane where the matchline is location. Record this value and break the loop. Also find the first slice where the ptv_body contour starts and record this value
                        Structure matchline = selectedSS.Structures.First(x => x.Id.ToLower() == "matchline");
                        bool lowLimNotFound = true;
                        int lowLim = -1;
                        if (!matchline.IsEmpty)
                        {
                            int matchplaneLocation = 0;
                            for (int i = 0; i != selectedSS.Image.ZSize - 1; i++)
                            {
                                if (matchline.GetContoursOnImagePlane(i).Any())
                                {
                                    matchplaneLocation = i;
                                    break;
                                }
                                if (lowLimNotFound && tmp1.GetContoursOnImagePlane(i).Any())
                                {
                                    lowLim = i;
                                    lowLimNotFound = false;
                                }
                            }

                            if (selectedSS.Structures.Where(x => x.Id.ToLower() == "dummybox").Any()) selectedSS.RemoveStructure(selectedSS.Structures.First(x => x.Id.ToLower() == "dummybox"));
                            Structure dummyBox = selectedSS.AddStructure("CONTROL", "DummyBox");

                            //get min/max positions of ptv_body contour to contour the dummy box for creating TS_PTV_Legs
                            Point3DCollection ptv_bodyPts = tmp1.MeshGeometry.Positions;
                            double xMax = ptv_bodyPts.Max(p => p.X) + 50.0;
                            double xMin = ptv_bodyPts.Min(p => p.X) - 50.0;
                            double yMax = ptv_bodyPts.Max(p => p.Y) + 50.0;
                            double yMin = ptv_bodyPts.Min(p => p.Y) - 50.0;

                            //box with contour points located at (x,y), (x,0), (x,-y), (0,-y), (-x,-y), (-x,0), (-x, y), (0,y)
                            VVector[] pts = new[] {
                                        new VVector(xMax, yMax, 0),
                                        new VVector(xMax, 0, 0),
                                        new VVector(xMax, yMin, 0),
                                        new VVector(0, yMin, 0),
                                        new VVector(xMin, yMin, 0),
                                        new VVector(xMin, 0, 0),
                                        new VVector(xMin, yMax, 0),
                                        new VVector(0, yMax, 0)};

                            //give 5cm margin on TS_PTV_LEGS (one slice of the CT should be 5mm) in case user wants to include flash up to 5 cm
                            for (int i = matchplaneLocation - 1; i > lowLim - 10; i--) dummyBox.AddContourOnImagePlane(pts, i);

                            //do the structure manipulation
                            if (selectedSS.Structures.Where(x => x.Id.ToLower() == "ts_ptv_legs").Any()) selectedSS.RemoveStructure(selectedSS.Structures.First(x => x.Id.ToLower() == "ts_ptv_legs"));
                            Structure TS_legs = selectedSS.AddStructure("CONTROL", "TS_PTV_Legs");
                            TS_legs.SegmentVolume = dummyBox.And(tmp.Margin(0));
                            //subtract both dummybox and matchline from TS_PTV_VMAT
                            tmp.SegmentVolume = tmp.Sub(dummyBox.Margin(0.0));
                            tmp.SegmentVolume = tmp.Sub(matchline.Margin(0.0));
                            //remove the dummybox structure if flash is NOT being used as its no longer needed
                            if (!useFlash) selectedSS.RemoveStructure(dummyBox);
                        }
                    }
                }
            }
            return false;
        }
    }
}
