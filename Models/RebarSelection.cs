using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RebarPlacement.Models
{
    public static class RebarSelection
    {
        public static void GetWallRebar(Document doc, Wall hostWall, XYZ wallOrientation, out List<Rebar> hostWallRebarHorizontal, out List<Rebar> hostWallRebarVertical)
        {
            //выбираем арматуру, принадлежащую указанной стене
            List<Rebar> hostWallRebar = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rebar)
                .WhereElementIsNotElementType()
                .Where(x => (x as Rebar).GetHostId().IntegerValue == hostWall.Id.IntegerValue)
                .Cast<Rebar>()
                .ToList();

            //Выполняем поиск вертикальной и горизонтальной арматуры
            hostWallRebarHorizontal = new List<Rebar>();
            hostWallRebarVertical = new List<Rebar>();

            /*  Отдельный котёл в аду для тех, кто придумал точность построений в Ревит.
             *  Порядочно намучался прежде, чем понял, что в Ревит 0 - это иногда что-то в -15 степени.
             *  Поэтому ввёл округления в проверках на перпендикулярность/параллельность.
             *  В горизонтально-вертикальной отрисовке это почти не имеет смысла - можно было проще задавать направление,
             *  но это понадобится в момент перехода к стенам, отрисованным под углом, когда нет параллельности к одной из координатных осей.
             */
            foreach (Rebar rebar in hostWallRebar)
            {
                IList<Curve> rebarCurves = rebar.GetCenterlineCurves(true, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0);

                //вычисляем вектор стержня
                XYZ curveVector = new XYZ(rebarCurves[0].GetEndPoint(1).X - rebarCurves[0].GetEndPoint(0).X,
                    rebarCurves[0].GetEndPoint(1).Y - rebarCurves[0].GetEndPoint(0).Y,
                    rebarCurves[0].GetEndPoint(1).Z - rebarCurves[0].GetEndPoint(0).Z
                    );

                //условия перпендикулярности
                if ((curveVector.Z != 0) && (curveVector.X * wallOrientation.X + curveVector.Y * wallOrientation.Y + curveVector.Z * wallOrientation.Z == 0))
                {
                    hostWallRebarVertical.Add(rebar);
                }
                //условия параллельности
                else if ((curveVector.Z == 0))
                {
                    try
                    {
                        if ((Math.Round(curveVector.X, 10) == 0 && Math.Round(wallOrientation.X, 10) == 0) ||
                            (Math.Round(curveVector.Y, 10) == 0 && Math.Round(wallOrientation.Y, 10) == 0))
                        {
                            hostWallRebarHorizontal.Add(rebar);
                        }
                        else if (Math.Round(Math.Abs(curveVector.X / wallOrientation.X), 10) == Math.Round(Math.Abs(curveVector.Y / wallOrientation.Y), 10))
                        {
                            hostWallRebarHorizontal.Add(rebar);
                        }
                    }
                    catch (DivideByZeroException ex)
                    {
                        continue;
                    }
                }
            }
        }
    }
}
