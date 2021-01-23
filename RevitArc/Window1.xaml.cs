using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using HelixToolkit.Wpf;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;



namespace RevitArc
{
    /// <summary>
    /// Window1.xaml 的交互逻辑
    /// </summary>
    public partial class Window1 : Window
    {

        public bool StartPointSelected;
        public bool EndPointSelected;
        public bool Done;
        public double Spacing;
        public double Height;

        public Window1()
        {
            InitializeComponent();


            //模型导入器
            ModelImporter modelImporter = new ModelImporter();

            //设置材料颜色
            Material material = new DiffuseMaterial(new SolidColorBrush(Colors.AliceBlue));
            modelImporter.DefaultMaterial = material;

            //三维模型导入
            Model3D Model = modelImporter.Load(@"C:\Users\zyx\Desktop\2RevitArcBridge\RevitArc\RevitArc\source\Chord.obj");

            //和modelview设置binding
            Binding binding = new Binding() { Source = Model };
            this.helixviewport.SetBinding(HelixViewport3D.DataContextProperty, binding);
        }

        private void ArcStartPointSelect(object sender, RoutedEventArgs e)
        {
            StartPointSelected = true;
            this.window.Hide();
        }

        private void ArcEndPointSelect(object sender, RoutedEventArgs e)
        {
            EndPointSelected = true;
            this.window.Hide();
        }


        private void DoneClick(object sender, RoutedEventArgs e)
        {
            Spacing = Convert.ToDouble(this.TB.Text);
            Height = Convert.ToDouble(this.TB2.Text);

            Done = true;
            DialogResult = true;
        }


    }
}
