﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VMATTBICSIAutoPlanningHelpers.Enums;

namespace VMATTBICSIAutoPlanningHelpers.UtilityClasses
{
    public class RequestedPlanMetric
    {
        public string StructureId { get; set; } = string.Empty;
        public DVHMetric DVHMetric { get; set; } = DVHMetric.None;
        public double QueryValue { get; set; } = double.NaN;
        public Units QueryUnits { get; set; } = Units.None;
        public Units QueryResultUnits { get; set; } = Units.None;

        public RequestedPlanMetric(string structureId, DVHMetric dVHMetric, double queryVal, Units queryUnits, Units resultUnits)
        {
            StructureId = structureId;
            DVHMetric = dVHMetric;
            QueryValue = queryVal;
            QueryUnits = queryUnits;
            QueryResultUnits = resultUnits;
        }

        public RequestedPlanMetric(string structureId, DVHMetric dVHMetric, Units resultUnits)
        {
            StructureId = structureId;
            DVHMetric = dVHMetric;
            QueryResultUnits = resultUnits;
        }
    }
}
