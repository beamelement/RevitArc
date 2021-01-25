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


            FamilySymbol chordFamilySymbol;
            FamilySymbol webMemberFamilySymbol;
            //1、载入弦杆族
            //2、载入腹杆族
            using (Transaction tran = new Transaction(uiDoc.Document))
            {
                tran.Start("载入族");
                //载入弦杆族
                string file = @"C:\Users\zyx\Desktop\2RevitArcBridge\RevitArc\RevitArc\source\chordFamlily.rfa";
                chordFamilySymbol = loadFaimly(file, commandData);
                chordFamilySymbol.Activate();

                //载入腹杆族
                file = @"C:\Users\zyx\Desktop\2RevitArcBridge\RevitArc\RevitArc\source\webMemberFamily.rfa";
                webMemberFamilySymbol = loadFaimly(file, commandData);
                webMemberFamilySymbol.Activate();

                tran.Commit();
            }

            //通过拉格朗日插值法计算出拱轴线
            List<XYZ> points = Largrange(ControlPoint);


            //求偏移向量并对其归一化,预备给之后横向偏移用
            XYZ normal;
            //先求向量
            XYZ line1 = new XYZ(points[points.Count - 1].X - points[0].X, points[points.Count - 1].Y - points[0].Y, points[points.Count - 1].Z - points[0].Z);
            XYZ temp = line1.CrossProduct(XYZ.BasisZ);
            //向量归一化,即把向量的模化作是1
            double tempLength = temp.GetLength();
            normal = new XYZ(temp.X / tempLength, temp.Y / tempLength, temp.Z / tempLength);


            //拱轴线上下各偏移界面高度减去直径,这个值是否勾到界面上去到时候再回来看好了
            double h = 2.65 * 3.28/2;

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

            //实例化第一个拱（通过自适应族）
            using (Transaction tran = new Transaction(uiDoc.Document))
            {
                tran.Start("弦杆族实例化");

                //将族实例化，并调整自适应点
                CreateFamilyInstance(UPlist, UPlist, chordFamilySymbol, commandData, false);

                CreateFamilyInstance(DOWNlist, DOWNlist, chordFamilySymbol, commandData, false);


                tran.Commit();
            }


            //实例化第一个拱腹杆
            using (Transaction tran = new Transaction(uiDoc.Document))
            {
                tran.Start("实例化腹杆");

                //腹杆间距
                double spacing = 1 * 3.28 / 2;

                //确定点并实例化斜、竖腹杆
                for (int j = 0; j < 2; j += 1)
                {
                    List<XYZ> tempUP = new List<XYZ>();
                    List<XYZ> tempDOWN = new List<XYZ>();

                    for (int i = 0; i < points.Count; i += 1)
                    {
                        double length = spacing;

                        XYZ p = UPlist[i];
                        XYZ pUP = new XYZ(p.X + (2 * j - 1) * length * normal.X, p.Y + (2 * j - 1) * length * normal.Y, p.Z + (2 * j - 1) * length * normal.Z);
                        tempUP.Add(pUP);

                        p = DOWNlist[i];
                        XYZ pDOWN = new XYZ(p.X + (2 * j - 1) * length * normal.X, p.Y + (2 * j - 1) * length * normal.Y, p.Z + (2 * j - 1) * length * normal.Z);
                        tempDOWN.Add(pDOWN);

                    }
                    CreateFamilyInstance(tempUP, tempDOWN, webMemberFamilySymbol, commandData, true);

                    //确定斜腹杆点位并实例化
                    //点位制定策略，上弦杆最高的点丢，下弦杆最低点重复一次
                    int ZMAXindex = getZMAXIndex(tempDOWN);
                    tempDOWN.Insert(ZMAXindex, tempDOWN[ZMAXindex]);

                    ZMAXindex = getZMAXIndex(tempUP);
                    tempUP.RemoveAt(ZMAXindex);
                    CreateFamilyInstance(tempUP, tempDOWN, webMemberFamilySymbol, commandData, false);
                }

                tran.Commit();
            }



            List<XYZ> UPlist2 = new List<XYZ>();
            List<XYZ> DOWNlist2 = new List<XYZ>();
            //实例化第二个拱（通过自适应族）
            using (Transaction tran = new Transaction(uiDoc.Document))
            {
                tran.Start("弦杆族实例化");

                //生成另一个拱的点
                for (int i = 0; i < points.Count; i += 1)
                {
                    double length = window1.Spacing;
                    XYZ p = UPlist[i];
                    XYZ pUP = new XYZ(p.X + length * normal.X, p.Y + length * normal.Y, p.Z + length * normal.Z);
                    UPlist2.Add(pUP);

                    p = DOWNlist[i];
                    XYZ pDOWN = new XYZ(p.X + length * normal.X, p.Y + length * normal.Y, p.Z + length * normal.Z);
                    DOWNlist2.Add(pDOWN);
                }

                //将族实例化，并调整自适应点
                CreateFamilyInstance(UPlist2, UPlist2, chordFamilySymbol, commandData, false);
                CreateFamilyInstance(DOWNlist2, DOWNlist2, chordFamilySymbol, commandData, false);

                tran.Commit();
            }


            //实例化第二个拱腹杆
            using (Transaction tran = new Transaction(uiDoc.Document))
            {
                tran.Start("实例化腹杆");

                //腹杆间距
                double spacing = 1 * 3.28 / 2;

                //实例化斜、竖腹杆
                for (int j = 0; j < 2; j += 1)
                {
                    List<XYZ> tempUP = new List<XYZ>();
                    List<XYZ> tempDOWN = new List<XYZ>();
                    for (int i = 0; i < points.Count; i += 1)
                    {
                        double length = spacing;

                        XYZ p = UPlist2[i];
                        XYZ pUP = new XYZ(p.X + (2 * j - 1) * length * normal.X, p.Y + (2 * j - 1) * length * normal.Y, p.Z + (2 * j - 1) * length * normal.Z);
                        tempUP.Add(pUP);

                        p = DOWNlist2[i];
                        XYZ pDOWN = new XYZ(p.X + (2 * j - 1) * length * normal.X, p.Y + (2 * j - 1) * length * normal.Y, p.Z + (2 * j - 1) * length * normal.Z);
                        tempDOWN.Add(pDOWN);

                    }
                    //实例化数值腹杆
                    CreateFamilyInstance(tempUP, tempDOWN, webMemberFamilySymbol, commandData, true);

                    //确定斜腹杆点位并实例化
                    //点位制定策略，上弦杆最高的点丢，下弦杆最低点重复一次
                    int ZMAXindex = getZMAXIndex(tempDOWN);
                    tempDOWN.Insert(ZMAXindex, tempDOWN[ZMAXindex]);

                    ZMAXindex = getZMAXIndex(tempUP);
                    tempUP.RemoveAt(ZMAXindex);
                    CreateFamilyInstance(tempUP, tempDOWN, webMemberFamilySymbol, commandData, false);
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





        //获取列表中最高点的索引值
        private int getZMAXIndex(List<XYZ> xYZs)
        {
            double Zmax = xYZs[0].Z;
            int ZMAXIndex = 0;
            for (int i = 0; i < xYZs.Count; i += 1)
            {
                if (Zmax < xYZs[i].Z)
                {
                    Zmax = xYZs[i].Z;
                    ZMAXIndex = i;
                }

            }

            return ZMAXIndex;
        }

        //创建族实例
        private void CreateFamilyInstance(List<XYZ> points1, List<XYZ> points2, FamilySymbol FamilySymbol,ExternalCommandData commandData,bool v)
        {

            //最后这个bool设定是为了设定两个list是否错开，是一一对应，还是错开一个相加


            UIDocument uiDoc = commandData.Application.ActiveUIDocument;           //取得当前活动文档    

            if (v)
            {
                //如果不错开
                for (int i = 0; i < points1.Count; i += 1)
                {
                    FamilyInstance familyInstance = AdaptiveComponentInstanceUtils.CreateAdaptiveComponentInstance(uiDoc.Document, FamilySymbol);
                    IList<ElementId> adaptivePoints = AdaptiveComponentInstanceUtils.GetInstancePlacementPointElementRefIds(familyInstance);
                    //取得的参照点
                    ReferencePoint referencePoint1 = uiDoc.Document.GetElement(adaptivePoints[0]) as ReferencePoint;
                    ReferencePoint referencePoint2 = uiDoc.Document.GetElement(adaptivePoints[1]) as ReferencePoint;
                    //设置参照点坐标
                    referencePoint1.Position = points1[i];
                    referencePoint2.Position = points2[i];
                }

            }
            else
            {
                //错开一个相加
                for (int i = 0; i < points1.Count - 1; i += 1)
                {
                    FamilyInstance familyInstance = AdaptiveComponentInstanceUtils.CreateAdaptiveComponentInstance(uiDoc.Document, FamilySymbol);
                    IList<ElementId> adaptivePoints = AdaptiveComponentInstanceUtils.GetInstancePlacementPointElementRefIds(familyInstance);
                    //取得的参照点
                    ReferencePoint referencePoint1 = uiDoc.Document.GetElement(adaptivePoints[0]) as ReferencePoint;
                    ReferencePoint referencePoint2 = uiDoc.Document.GetElement(adaptivePoints[1]) as ReferencePoint;
                    //设置参照点坐标
                    referencePoint1.Position = points1[i];
                    referencePoint2.Position = points2[i + 1];
                }
            }
        }

        //载入族
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

        //创建模型线
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

        //计算拉格朗日线
        private List<XYZ> Largrange(List<XYZ> xYZs)
        {
            List<XYZ> xyzList = new List<XYZ>();
            //获取抛物线首尾两点连线及其向量
            Line line = Line.CreateBound(xYZs[0], xYZs[xYZs.Count - 1]);
            XYZ normal = line.Direction;


            for (double x = xYZs[0].X; x < xYZs[xYZs.Count - 1].X; x += 2.75*3.28)
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

        //选择两条交叉的线求交点
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
