using System.Collections.Generic;

namespace VMATTBICSIAutoPlanningHelpers.Helpers
{
    public static class IsoNameHelper
    {
        /// <summary>
        /// Simple method to specify the isocenter names for VMAT CSI
        /// </summary>
        /// <param name="numVMATIsos"></param>
        /// <returns></returns>
        public static List<string> GetCSIIsoNames(int numVMATIsos)
        {
            List<string> isoNames = new List<string> { };
            isoNames.Add("Brain");
            if (numVMATIsos > 1)
            {
                if (numVMATIsos > 2) isoNames.Add("UpSpine");
                isoNames.Add("LowSpine");
            }
            return isoNames;
        }

        /// <summary>
        /// Helper method to specify the VMAT isocenter names based on the supplied number of vmat isos and total number of isos
        /// </summary>
        /// <param name="numVMATIsos"></param>
        /// <param name="numIsos"></param>
        /// <returns></returns>
        public static List<string> GetTBIVMATIsoNames(int numVMATIsos, int numIsos)
        {
            // TODO: Stop Hardcoding iso names 
            List<string> isoNames = new List<string> { };
            isoNames.Add("Head");
            if (numVMATIsos > 1 || numIsos > 1)
            {
                if (numIsos > numVMATIsos)
                {
                    if (numVMATIsos == 2) isoNames.Add("Pelvis");
                    else
                    {
                        isoNames.Add("Chest");
                        if (numVMATIsos == 3) isoNames.Add("Pelvis");
                        else if (numVMATIsos == 4)
                        {
                            isoNames.Add("Abdomen");
                            isoNames.Add("Pelvis");
                        }
                    }
                }
                else
                {
                    isoNames.Add("Chest");
                    switch (numVMATIsos)
                    {
                        case 3:
                            isoNames.Add("Pelvis");
                            break;
                        case 4:
                            isoNames.Add("Pelvis");
                            isoNames.Add("Legs");
                            break;
                        case 5:
                            isoNames.Add("Pelvis");
                            isoNames.Add("Upper Legs");
                            isoNames.Add("Lower Legs");
                            break;
                        case 6:
                            isoNames.Add("Pelvis");
                            isoNames.Add("Upper Legs");
                            isoNames.Add("Lower Legs");
                            isoNames.Add("Feet");
                            break;
                        case 7:
                            isoNames.Add("Abdomen");
                            isoNames.Add("Pelvis");
                            isoNames.Add("Upper Legs");
                            isoNames.Add("Lower Legs");
                            isoNames.Add("Feet");
                            break;
                        default:
                            break;
                    }
                }

            }


            return isoNames;
        }

        /// <summary>
        /// Helper method to specify the AP/PA isocenter names based on the supplied number of vmat isos and total number of isos
        /// </summary>
        /// <param name="numVMATIsos"></param>
        /// <param name="numIsos"></param>
        /// <returns></returns>
        public static List<string> GetTBIAPPAIsoNames(int numVMATIsos, int numIsos)
        {
            List<string> isoNames = new List<string> { };
            isoNames.Add("AP / PA upper legs");
            if (numIsos == numVMATIsos + 2) isoNames.Add("AP / PA lower legs");
            return isoNames;
        }
    }
}
