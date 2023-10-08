using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RebarPlacement.Models.ElementComparers
{
    class XYZComparer : IComparer<XYZ>
    {
        public XYZ BasePoint { get; set; }

        public int Compare(XYZ point1, XYZ point2)
        {
            //return String.Compare(d1.Mark, d2.Mark);
            double distance1 = point1.DistanceTo(BasePoint);
            double distance2 = point2.DistanceTo(BasePoint);

            if (distance1 < distance2)
            {
                return -1;
            }
            else if (distance1 == distance2)
            {
                return 0;
            }
            else
            {
                return 1;
            }
        }
    }
}
