using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RebarPlacement
{
    [TransactionAttribute(TransactionMode.Manual)]
    public class RebarPlacement : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uiDoc = commandData.Application.ActiveUIDocument;
                Document doc = uiDoc.Document;

                //выбор текущего вида (в целях самопроверки)
                View3D view = uiDoc.ActiveView as View3D;

                //выбор арматурного стержня для копирования
                Reference selectedRebarElementRef = uiDoc.Selection.PickObject(ObjectType.Element, new RebarSelectionFilter(), "Выберите исходную арматурную шпильку");
                Rebar baseRebar = doc.GetElement(selectedRebarElementRef) as Rebar;

                //выбор стены для размещения стержней
                Reference selectedWallElementRef = uiDoc.Selection.PickObject(ObjectType.Element, new WallSelectionFilter(), "Выберите базовую стену");
                Wall hostWall = doc.GetElement(selectedWallElementRef) as Wall;

                //получение параметров для создания арматуры в выбранной стене
                RebarShape rebarShape = doc.GetElement(baseRebar.GetAllRebarShapeIds()[0]) as RebarShape;
                RebarBarType rebarBarType = doc.GetElement(baseRebar.GetTypeId()) as RebarBarType;

                LocationCurve wallLocation = hostWall.Location as LocationCurve;
                XYZ origin = (wallLocation.Curve).GetEndPoint(0);

                XYZ xVec = hostWall.Orientation;
                XYZ yVec = new XYZ(0, 0, -1);

                XYZ wallOrientation = new XYZ((wallLocation.Curve).GetEndPoint(1).X - (wallLocation.Curve).GetEndPoint(0).X,
                        (wallLocation.Curve).GetEndPoint(1).Y - (wallLocation.Curve).GetEndPoint(0).Y,
                        (wallLocation.Curve).GetEndPoint(1).Z - (wallLocation.Curve).GetEndPoint(0).Z
                        );

                //выбираем арматуру, принадлежащую указанной стене
                List<Rebar> hostWallRebar = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rebar)
                    .WhereElementIsNotElementType()
                    .Where(x => (x as Rebar).GetHostId().IntegerValue == hostWall.Id.IntegerValue)
                    .Cast<Rebar>()
                    .ToList();

                //Выполняем поиск вертикальной и горизонтальной арматуры
                List<Rebar> hostWallRebarHorizontal = new List<Rebar>();
                List<Rebar> hostWallRebarVertical = new List<Rebar>();

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

                    XYZ laserOriginPoint = originPoint - 5 * xVec;

                    ReferenceIntersector referenceIntersector = new ReferenceIntersector(hostWall.Id, FindReferenceTarget.Face, view);
                    IList<ReferenceWithContext> intersectedReferences = referenceIntersector.Find(originPoint, hostWall.Orientation);

                    if (intersectedReferences.Count != 0)
                    {
                        insertionPoints.Add(originPoint);
                    }
                }

                /*  Создание арматуры в выбранной стене
                 */
                #region Rebar_Creation_Transaction
                Transaction transaction = new Transaction(doc);

                transaction.Start("Create rebar");

                foreach (XYZ insertionPoint in insertionPoints)
                {
                    Rebar rebar = Rebar.CreateFromRebarShape(doc, rebarShape, rebarBarType, hostWall, insertionPoint, xVec, yVec);

                    //for testing purposes
                    rebar.SetUnobscuredInView(view, true);
                    rebar.SetSolidInView(view, true);
                }

                transaction.Commit();
                #endregion
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }

            return Result.Succeeded;
        }
    }

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

    public class RebarSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element element)
        {
            return element is Rebar;
        }

        public bool AllowReference(Reference refer, XYZ point)
        {
            return false;
        }
    }

    public class WallSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element element)
        {
            return element is Wall;
        }

        public bool AllowReference(Reference refer, XYZ point)
        {
            return false;
        }
    }
}