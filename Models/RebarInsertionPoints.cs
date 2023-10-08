using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RebarPlacement.Models.ElementComparers;
using System;
using System.Collections.Generic;

namespace RebarPlacement.Models
{
    class RebarInsertionPoints
    {
        public static List<XYZ> GetInsertionPoints(View3D view, Wall hostWall, LocationCurve wallLocation, XYZ displacementVector, List<Rebar> hostWallRebarHorizontal, List<Rebar> hostWallRebarVertical)
        {
            //Производим поиск проекций точек для вставки шпильки в плоскости X0Y с сортировкой по расстоянию от начала стены
            XYZComparer xyzComparer = new XYZComparer();
            xyzComparer.BasePoint = (wallLocation.Curve).GetEndPoint(0);
            SortedSet<XYZ> middlePointsProjection = new SortedSet<XYZ>(xyzComparer);

            List<XYZ> verticalRebarLocations = new List<XYZ>();

            //создаём новые объекты Curve с помощью метода CreateOffset для привязки ко всем элементам групп стержней
            foreach (Rebar rebar in hostWallRebarVertical)
            {
                for (int i = 0; i < rebar.NumberOfBarPositions; i++)
                {
                    XYZ offsetVec = rebar.GetShapeDrivenAccessor().GetBarPositionTransform(i).Origin;
                    IList<Curve> rebarCurvesVertical = rebar.GetCenterlineCurves(true, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                    verticalRebarLocations.Add(rebarCurvesVertical[0].GetEndPoint(0) + offsetVec);
                }
            }


            while (verticalRebarLocations.Count > 0)
            {
                //рассматриваем кривую первого стержня и образующую стены
                IntersectionResult intersectionResultFirst = wallLocation.Curve.Project(verticalRebarLocations[0]);
                XYZ firstProjectedPoint = intersectionResultFirst.XYZPoint;    //первая проекция на образующую кривую стены

                int i = 1;
                while (i < verticalRebarLocations.Count)
                {
                    //рассматриваем кривые других стержней и образующую стены
                    IntersectionResult intersectionResultSecond = wallLocation.Curve.Project(verticalRebarLocations[i]);
                    XYZ secondProjectedPoint = intersectionResultSecond.XYZPoint;     //вторая проекция на образующую кривую стены

                    //если прямые проецируются в одну точку...
                    if (Math.Round(firstProjectedPoint.X, 10) == Math.Round(secondProjectedPoint.X, 10)
                        && Math.Round(firstProjectedPoint.Y, 10) == Math.Round(secondProjectedPoint.Y, 10))
                    {
                        XYZ firstPoint = verticalRebarLocations[0];
                        XYZ secondPoint = verticalRebarLocations[i];

                        //... и если прямые не лежат на одной линии
                        if (Math.Round(firstPoint.X, 10) == Math.Round(secondPoint.X, 10)
                        && Math.Round(firstPoint.Y, 10) == Math.Round(secondPoint.Y, 10))
                        {
                            i++;
                            continue;
                        }

                        middlePointsProjection.Add(new XYZ((firstPoint.X + secondPoint.X) / 2, (firstPoint.Y + secondPoint.Y) / 2, 0));
                        verticalRebarLocations.RemoveAt(i);
                        break;
                    }

                    i++;
                }

                verticalRebarLocations.RemoveAt(0);
            }

            //сортированное множество координат Z
            SortedSet<XYZ> zCoordinates = new SortedSet<XYZ>(xyzComparer);
            foreach (Rebar rebar in hostWallRebarHorizontal)
            {
                for (int i = 0; i < rebar.NumberOfBarPositions; i++)
                {
                    XYZ offsetVec = rebar.GetShapeDrivenAccessor().GetBarPositionTransform(i).Origin;
                    IList<Curve> rebarCurvesHorizontal = rebar.GetCenterlineCurves(true, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                    zCoordinates.Add(new XYZ(0, 0, (rebarCurvesHorizontal[0].GetEndPoint(0) + offsetVec).Z));
                }
            }

            //переходим к списку для возможности реализации шахматной раскладки
            List<XYZ> xyCoordinates = new List<XYZ>();
            foreach (XYZ point in middlePointsProjection)
            {
                xyCoordinates.Add(point);
            }

            /*  Создание списка точек для привязки арматурных шпилек в шахматном порядке
             *  Ремарка: программа верно находит центр между парными стержнями, 
             *  но помимо этого должно задаваться какое-то смещение,
             *  т.к. привязка стержня производится не к геометрическому центру.
             *  
             *  Смещение можно задавать отдельной переменной в зависимости от параметров стержня, 
             *  чтобы не усложнять список точек вставки.
             */
            List<XYZ> possibleInsertionPoints = new List<XYZ>();
            int placementSwitch = 0;  //переключатель для расположения в шахматном порядке
            foreach (XYZ zCoord in zCoordinates)
            {
                for (int i = 0; 2 * i + placementSwitch % 2 < xyCoordinates.Count; i++)
                {
                    XYZ xyCoord = xyCoordinates[2 * i + placementSwitch % 2];
                    XYZ insertionPoint = new XYZ(xyCoord.X, xyCoord.Y, zCoord.Z);
                    possibleInsertionPoints.Add(insertionPoint);
                }

                placementSwitch++;
            }

            /*  Оказалось, что искать проще искать пересечение с гранью стены, чем само отверстие.
             *  Данный кусок кода должен искать точки размещения шпилек только там, где нет отверстий.
             *  Для этого создаём новый список, чтобы транзакцию провести только один раз.
             */
            List<XYZ> insertionPoints = new List<XYZ>();

            foreach (XYZ originPoint in possibleInsertionPoints)
            {
                ElementFilter elementFilter = new ElementClassFilter(typeof(Wall));

                XYZ laserOriginPoint = originPoint - 5 * displacementVector;

                ReferenceIntersector referenceIntersector = new ReferenceIntersector(hostWall.Id, FindReferenceTarget.Face, view);
                IList<ReferenceWithContext> intersectedReferences = referenceIntersector.Find(originPoint, hostWall.Orientation);

                if (intersectedReferences.Count != 0)
                {
                    insertionPoints.Add(originPoint);
                }
            }

            return insertionPoints;
        }
    }
}
