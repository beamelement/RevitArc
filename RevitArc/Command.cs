using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Application = Autodesk.Revit.ApplicationServices.Application;

namespace RevitArc
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        Result IExternalCommand.Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            //阐述一下思路，就是现在XZ平面上生成一条拉格朗日插值法生成曲线
            //再把他投影到起终点连线的那个平面上去（就是改一下Y坐标 其余不变）

            XYZ SPoint = null;
            XYZ EPoint = null;
            XYZ TopPoint = null;
            List<XYZ> ControlPoint = new List<XYZ>();

            Document revitDoc = commandData.Application.ActiveUIDocument.Document;  //取得文档           
            Application revitApp = commandData.Application.Application;             //取得应用程序            
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;           //取得当前活动文档        

            Window1 window1 = new Window1();

            if (window1.ShowDialog() == true)
            {
                //窗口打开并停留，只有点击按键之后，窗口关闭并返回true
            }


            //按键会改变window的属性，通过对属性的循环判断来实现对按键的监测
            while (!window1.Done)
            {

                //选择起点
                if (window1.StartPointSelected)
                {
                    SPoint = SelectPoint(commandData);
                    window1.StartPointSelected = false;
                }

                if (window1.ShowDialog() == true)
                {
                    //窗口打开并停留，只有点击按键之后，窗口关闭并返回true
                }

                //选择终点
                if (window1.EndPointSelected)
                {
                    EPoint = SelectPoint(commandData);
                    window1.EndPointSelected = false;
                }

                if (window1.ShowDialog() == true)
                {
                    //依据起终点生成以及矢高生成顶点
                    TopPoint = new XYZ((SPoint.X + EPoint.X) / 2, (SPoint.Y + EPoint.Y) / 2, (SPoint.Z + EPoint.Z) / 2 + window1.Height);
                    ControlPoint.Add(SPoint);
                    ControlPoint.Add(TopPoint);
                    ControlPoint.Add(EPoint);
                }
            }

            //通过拉格朗日插值法计算出拱轴线
            List<XYZ> points = Largrange(ControlPoint);

            //拱轴线上下各偏移界面高度减去直径
            double h = 2.65/2 * 3.28;

            List<XYZ> UPlist = new List<XYZ>();
            List<XYZ> DOWNlist = new List<XYZ>();
            for (int i = 0; i < points.Count; i += 1)
            {

                XYZ p = points[i];
                XYZ pUP = new XYZ(p.X, p.Y, p.Z + h);
                UPlist.Add(pUP);

                XYZ pDOWN = new XYZ(p.X, p.Y, p.Z - h);
                DOWNlist.Add(pDOWN);
            }


            //将弦杆族载入项目文件，并进行实例化
            using (Transaction tran = new Transaction(uiDoc.Document))
            {
                tran.Start("载入弦杆族");


                //载入族
                string file = @"C:\Users\zyx\Desktop\2RevitArcBridge\钢管混凝土构件库\chordFamlily.rfa";
                FamilySymbol adaptiveFamilySymbol = loadFaimly(file, commandData);
                adaptiveFamilySymbol.Activate();


                //将族实例化，并调整自适应点
                for (int i = 0; i < points.Count - 1; i += 1)
                {
                    FamilyInstance familyInstance = AdaptiveComponentInstanceUtils.CreateAdaptiveComponentInstance(uiDoc.Document, adaptiveFamilySymbol);
                    IList<ElementId> adaptivePoints = AdaptiveComponentInstanceUtils.GetInstancePlacementPointElementRefIds(familyInstance);
                    ReferencePoint referencePoint1 = uiDoc.Document.GetElement(adaptivePoints[0]) as ReferencePoint;
                    ReferencePoint referencePoint2 = uiDoc.Document.GetElement(adaptivePoints[1]) as ReferencePoint;
                    referencePoint1.Position = UPlist[i];
                    referencePoint2.Position = UPlist[i + 1];
                }

                for (int i = 0; i < points.Count - 1; i += 1)
                {
                    FamilyInstance familyInstance = AdaptiveComponentInstanceUtils.CreateAdaptiveComponentInstance(uiDoc.Document, adaptiveFamilySymbol);
                    IList<ElementId> adaptivePoints = AdaptiveComponentInstanceUtils.GetInstancePlacementPointElementRefIds(familyInstance);
                    ReferencePoint referencePoint1 = uiDoc.Document.GetElement(adaptivePoints[0]) as ReferencePoint;
                    ReferencePoint referencePoint2 = uiDoc.Document.GetElement(adaptivePoints[1]) as ReferencePoint;
                    referencePoint1.Position = DOWNlist[i];
                    referencePoint2.Position = DOWNlist[i + 1];
                }


                tran.Commit();
            }


            //创建模型线了
            //让这条线以模型线的形式展示一下

            using (Transaction tran = new Transaction(uiDoc.Document))
            {
                tran.Start("创建模型线");

                //CreateModelLine(UPlist,commandData);
                //CreateModelLine(DOWNlist,commandData);
                CreateModelLine(points,commandData);

                tran.Commit();
            }

            return Result.Succeeded;
        }

        private FamilySymbol loadFaimly(string file,ExternalCommandData commandData)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;           //取得当前活动文档     
            bool loadSuccess = uiDoc.Document.LoadFamily(file, out Family family);
            //得到族模板，并激活
            ElementId elementId;
            ISet<ElementId> symbols = family.GetFamilySymbolIds();
            elementId = symbols.First();
            FamilySymbol adaptiveFamilySymbol = uiDoc.Document.GetElement(elementId) as FamilySymbol;
            return adaptiveFamilySymbol;
        }

        private void CreateModelLine(List<XYZ> points,ExternalCommandData commandData)
        {
            for (int i = 0; i < points.Count - 1; i += 1)
            {
                UIDocument uiDoc = commandData.Application.ActiveUIDocument;           //取得当前活动文档    

                XYZ PointStart = points[i];
                XYZ PointEnd = points[i + 1];
                //直线向量
                XYZ vector = new XYZ(PointStart.X - PointEnd.X, PointStart.Y - PointEnd.Y, PointStart.Z - PointEnd.Z);
                //向量和Z向量叉乘,从而获得一个必定与向量垂直的向量,并以此创建一个平面
                XYZ normal = vector.CrossProduct(XYZ.BasisZ);
                Plane plane = Plane.CreateByNormalAndOrigin(normal, PointStart);
                SketchPlane sketchPlane = SketchPlane.Create(uiDoc.Document, plane);

                uiDoc.Document.Create.NewModelCurve(Line.CreateBound(PointStart, PointEnd), sketchPlane);
            }
        }

        private List<XYZ> Largrange(List<XYZ> xYZs)
        {
            List<XYZ> xyzList = new List<XYZ>();
            //获取抛物线首尾两点连线及其向量
            Line line = Line.CreateBound(xYZs[0], xYZs[xYZs.Count - 1]);
            XYZ normal = line.Direction;


            for (double x = xYZs[0].X; x < xYZs[xYZs.Count - 1].X; x++)
            {
                double z = FZ(x);
                double tanCita = normal.Y / normal.X;
                double delY = (x - xYZs[0].X)*tanCita;

                XYZ xyz1 = new XYZ(x, xYZs[0].Y + delY, z);
                xyzList.Add(xyz1);
            }

            //把最后的一个点也给加上
            xyzList.Add(xYZs[xYZs.Count - 1]);
            return xyzList;

            //获取在XZ平面上的抛物线坐标点
            double FZ(double x)
            {
                double sum = 0;
                for (int k = 0; k < xYZs.Count; k++)
                {
                    sum += FX(x, k) * xYZs[k].Z;
                }
                return sum;
            }

            double FX(double x, int k)
            {
                double sum = 1;
                for (int i = 0; i < xYZs.Count; i++)
                {
                    if (i != k)
                    {
                        sum *= (x - xYZs[i].X) / (xYZs[k].X - xYZs[i].X);
                    }
                }
                return sum;
            }
        }

        private XYZ SelectPoint(ExternalCommandData commandData)
        {

            Document revitDoc = commandData.Application.ActiveUIDocument.Document;  //取得文档           
            Application revitApp = commandData.Application.Application;             //取得应用程序            
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;           //取得当前活动文档     


            Selection sel = uiDoc.Selection;
            Reference ref1 = sel.PickObject(ObjectType.Element, "选择一条模型线");
            Element elem = revitDoc.GetElement(ref1);
            ModelLine modelLine1 = elem as ModelLine;
            Curve curve1 = modelLine1.GeometryCurve;

            Reference ref2 = sel.PickObject(ObjectType.Element, "选择一条模型线");
            elem = revitDoc.GetElement(ref2);
            ModelLine modelLine2 = elem as ModelLine;
            Curve curve2 = modelLine2.GeometryCurve;

            curve1.Intersect(curve2, out IntersectionResultArray intersectionResultArray);
            XYZ Point = intersectionResultArray.get_Item(0).XYZPoint;

            return Point;

        }
    }
}
