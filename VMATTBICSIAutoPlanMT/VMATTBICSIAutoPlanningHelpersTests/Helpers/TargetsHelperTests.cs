﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using VMATTBICSIAutoPlanningHelpers.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VMS.TPS.Common.Model.Types;
using VMATTBICSIAutoPlanningHelpers.UtilityClasses;

namespace VMATTBICSIAutoPlanningHelpers.Helpers.Tests
{
    [TestClass()]
    public class TargetsHelperTests
    {
        public List<Prescription> CreateDummyPrescription()
        {
            //plan ID, target Id, numFx, dosePerFx, cumulative dose
            return new List<Prescription>
            {
                new Prescription("CSI-init", "PTV_CSIMid", 20, new DoseValue(160.0, DoseValue.DoseUnit.cGy), 3200.0),
                new Prescription("CSI-init", "PTV_CSI", 20, new DoseValue(180.0, DoseValue.DoseUnit.cGy), 3600.0),
                new Prescription("CSI-bst", "PTV_Boost", 10, new DoseValue(180.0, DoseValue.DoseUnit.cGy), 5400.0),
            };
        }

        [TestMethod()]
        public void GetHighestRxPlanTargetListTestRx()
        {
            List<Prescription> testRx = CreateDummyPrescription();
            List<Tuple<string, string>> expected = new List<Tuple<string, string>>
            {
                Tuple.Create("CSI-init", "PTV_CSI"),
                Tuple.Create("CSI-bst", "PTV_Boost"),
            };
            CollectionAssert.AreEqual(expected, TargetsHelper.GetHighestRxPlanTargetList(testRx));
        }
    }
}