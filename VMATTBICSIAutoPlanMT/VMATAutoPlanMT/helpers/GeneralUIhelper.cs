﻿using System;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.Windows.Controls;
using System.Windows;

namespace VMATAutoPlanMT.helpers
{
    public class GeneralUIhelper
    {
        //common to both structure tuning and optimization setup tabs
        public bool clearRow(object sender, StackPanel sp)
        {
            //same deal as the clear sparing structure button (clearStructBtn_click)
            Button btn = (Button)sender;
            int i = 0;
            int k = 0;
            foreach (object obj in sp.Children)
            {
                foreach (object obj1 in ((StackPanel)obj).Children)
                {
                    if (obj1.Equals(btn)) k = i;
                }
                if (k > 0) break;
                i++;
            }

            //clear entire list if there are only two entries (header + 1 real entry)
            if (sp.Children.Count < 3) { return true; }
            else sp.Children.RemoveAt(k);
            return false;
        }
    }
}
