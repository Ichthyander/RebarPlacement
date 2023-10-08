using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RebarPlacement.Models;
using RebarPlacement.Models.ElementComparers;
using RebarPlacement.Models.ElementSelectionUtils;
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

                BoundingBoxXYZ rebarBoundingBoxXYZ = baseRebar.get_BoundingBox(null);
                XYZ rebarDisplacementVector = new XYZ((rebarBoundingBoxXYZ.Min.X - rebarBoundingBoxXYZ.Max.X) / 2,
                                                        (rebarBoundingBoxXYZ.Min.Y - rebarBoundingBoxXYZ.Max.Y) / 2,
                                                        (rebarBoundingBoxXYZ.Max.Z - rebarBoundingBoxXYZ.Min.Z) / 2);

                //Выполняем поиск вертикальной и горизонтальной арматуры
                List <Rebar> hostWallRebarHorizontal, hostWallRebarVertical;
                RebarSelection.GetWallRebar(doc, hostWall, wallOrientation, out hostWallRebarHorizontal, out hostWallRebarVertical);

                //Выполняем поиск точек для расстановки арматуры
                List<XYZ> insertionPoints = RebarInsertionPoints.GetInsertionPoints(view, hostWall, wallLocation, hostWall.Orientation, hostWallRebarHorizontal, hostWallRebarVertical);

                /*  Создание арматуры в выбранной стене
                 */
                #region Rebar_Creation_Transaction
                Transaction transaction = new Transaction(doc);

                transaction.Start("Create rebar");

                //Rebar rebar = Rebar.CreateFromCurvesAndShape(doc, rebarShape, rebarBarType, null, null, hostWall, wallOrientation,
                //                                baseRebar.GetCenterlineCurves(true, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0),
                //                                RebarHookOrientation.Left, RebarHookOrientation.Right);

                ////for testing purposes
                //rebar.SetUnobscuredInView(view, true);
                //rebar.SetSolidInView(view, true);

                foreach (XYZ insertionPoint in insertionPoints)
                {
                    Rebar rebar = Rebar.CreateFromRebarShape(doc, rebarShape, rebarBarType, hostWall, insertionPoint + rebarDisplacementVector, xVec, yVec);

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
}