using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI.Selection;

namespace RebarPlacement.Models.ElementSelectionUtils
{
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
}
