﻿using System;
using System.Collections.Generic;
using System.Linq;
using VMATTBICSIAutoPlanningHelpers.UtilityClasses;
using VMS.TPS.Common.Model.Types;

namespace VMATTBICSIAutoPlanningHelpers.Helpers
{
    public static class RingHelper
    {
        /// <summary>
        /// Helper method to take the existing list of rings and rescale the dose levels to the doses listed in the prescriptions
        /// </summary>
        /// <param name="existingRings"></param>
        /// <param name="prescriptions"></param>
        /// <param name="oldInitRx"></param>
        /// <param name="newInitRx"></param>
        /// <param name="oldBstRx"></param>
        /// <param name="newBstRx"></param>
        /// <returns></returns>
        public static List<TSRing> RescaleRingDosesToNewRx(List<TSRing> existingRings,
                                                           List<Prescription> prescriptions,
                                                           double oldInitRx,
                                                           double newInitRx,
                                                           double oldBstRx,
                                                           double newBstRx)
        {
            List<TSRing> scaledRings = new List<TSRing> { };
            List<Tuple<string, double>> planIdRx = TargetsHelper.GetPlanIdHighesRxDoseFromPrescriptions(prescriptions);
            bool isInitPlan = true;

            foreach (TSRing itr in existingRings)
            {
                //match ring target to prescription target
                if (prescriptions.Any(x => string.Equals(x.TargetId, itr.TargetId)))
                {
                    if (planIdRx.Count > 1)
                    {
                        //multiple plan entries in planIdRx --> need to determine if this target belongs to the initial plan or boost plan
                        string planId = prescriptions.FirstOrDefault(x => string.Equals(x.TargetId, itr.TargetId)).PlanId;
                        if (string.Equals(planId, planIdRx.Last().Item1))
                        {
                            //plan id for this target matches the last entry in planIdRx --> belongs to boost plan
                            isInitPlan = false;
                        }
                    }
                    double scaleFactor;
                    if (isInitPlan)
                    {
                        //target belongs to initial plan
                        scaleFactor = newInitRx / oldInitRx;

                    }
                    else
                    {
                        //target belongs to boost plan
                        scaleFactor = newBstRx / oldBstRx;
                    }
                    //scale ring dose by ratio of appropriate plan Rx doses
                    scaledRings.Add(new TSRing(itr.TargetId, itr.MarginFromTargetInCM, itr.RingThicknessInCM, itr.DoseLevel * scaleFactor));
                }
            }

            return scaledRings;
        }
    }
}
