using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace RebarPlacement.Models.ElementSelectionUtils
{
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
