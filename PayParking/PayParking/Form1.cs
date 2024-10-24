using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Media;

using Emgu;
using Emgu.CV;
using Emgu.CV.Util;
using Emgu.CV.Structure;
using Emgu.CV.OCR;
using Emgu.Util;
using Emgu.CV.CvEnum;

using DirectShowLib;

using MySql.Data.MySqlClient;
using Mysqlx.Crud;
using Org.BouncyCastle.Asn1.Cmp;
using Org.BouncyCastle.Asn1.X509;
using System.Threading;


namespace PayParking
{
    public partial class Form1 : Form
    {
        private NumberOfCar plateRecognizer;

        private Point startPoint;

        private Mat InputImage;

        private VideoCapture Capture = null;
        private DsDevice[] WebCams = null;
        private int CameraId = 0;

        string connectionString = "Server=MySQL-8.2;Port=3306;Database=PayParking;Uid=user;Pwd=1;";
        MySqlConnection connection;

        SoundPlayer player = null;
        string SoundName = string.Empty;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            plateRecognizer = new NumberOfCar();
            player = new SoundPlayer();
            SoundName = @"alarm_emergencymeeting.wav";
            toolStripComboBox3.SelectedIndex = 0;
            WebCams = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            for (int i = 0; i < WebCams.Length; i++)
            {
                toolStripComboBox2.Items.Add(WebCams[i].Name);
            }
             connection = new MySqlConnection(connectionString); // подключаемся к бд на старте 
             connection.Open();
            if (connection.State == ConnectionState.Open) 
            {
                MessageBox.Show(connection.ToString());
            }// потом удалить 



        }

        public void ProcesseImage(IInputOutputArray image)
        {
            List <IInputOutputArray> licensePlateImageList = new List <IInputOutputArray>();
            List<IInputOutputArray> filteredLicensePlatImage = new List<IInputOutputArray>();
            List <RotatedRect> licensesBoxList = new List<RotatedRect>();

            List<string> recognizedPlates = plateRecognizer.DetectLicensePlate(image, licensePlateImageList, filteredLicensePlatImage, licensesBoxList);

            panel1.Controls.Clear();

            startPoint = new Point(20,50);

            for (int i = 0; i < recognizedPlates.Count;i++)
            {
                Mat dest = new Mat();

                CvInvoke.VConcat(licensePlateImageList[i], filteredLicensePlatImage[i], dest);

                AddLabelAndImage(recognizedPlates[i], dest);
            }
            Image <Bgr,byte> outputImage = InputImage.ToImage<Bgr,byte>();
            
            foreach (RotatedRect rect in licensesBoxList)
            {
                PointF[] v = rect.GetVertices();

                PointF prevPoint = v[0];
                PointF firstPoint = prevPoint;
                PointF nextPoint = prevPoint;
                PointF lastPoint = nextPoint;

                for (int i = 1; i < v.Length;i++)
                {
                    nextPoint = v[i];

                    CvInvoke.Line(outputImage,Point.Round(prevPoint),Point.Round(nextPoint),new MCvScalar(0,0,255),5,LineType.EightConnected,0);

                    prevPoint = nextPoint;
                    lastPoint = prevPoint;
                }

                CvInvoke.Line(outputImage, Point.Round(lastPoint),Point.Round(firstPoint),new MCvScalar(0,0,255),5, LineType.EightConnected, 0);
            }
            pictureBox1.Image = outputImage.Bitmap;
        }

        private void AddLabelAndImage (string lableText,IInputArray image) // добавление фото 
        {

             string FilteredText ="" + FilterOfText(lableText);
            if (FilteredText.Length >= 6)
            {
                string Number = FilteredText;
                DateTime dateTime = DateTime.Now;
                bool Sost = false; // false - въезд , true - выезд  
                bool PayStatus = false;  // false - не оплачено , true - оплачено   
                long TimeParking = long.Parse(DateTime.Now.Day.ToString())*24*60 + long.Parse(DateTime.Now.Hour.ToString())*60 + long.Parse(DateTime.Now.Minute.ToString());
                Label label1 = new Label(); 
                label1.Text = FilteredText;

                if (toolStripComboBox3.SelectedIndex == 0) // работа на въезд 
                {
                    label1.Width = 100;
                    label1.Height = 30;
                    label1.Location = startPoint;

                    startPoint.Y += label1.Height;
                    panel1.Controls.Add(label1);

                    PictureBox box = new PictureBox();
                    Mat m = image.GetInputArray().GetMat();

                    box.ClientSize = m.Size;
                    box.Image = m.Bitmap;
                    box.Location = startPoint;

                    startPoint.Y += box.Height + 10;
                    panel1.Controls.Add(box);
                    SoundEffect();
                    string query =  $"INSERT INTO PP(CarNumber,Time,Sostoyanie,PayStatus) VALUES('{label1.Text}','{TimeParking}',{Sost},{PayStatus})"; // текстовое представление запроса (
                    MySqlCommand command = new MySqlCommand(query, connection); // класс содержащий информацию о запросе
                    command.ExecuteNonQuery();//метод выполняющий запрос 

                }
                else // работа на выезд 
                {
                    Sost = true;
                    string query = $"SELECT Time FROM PP WHERE CarNumber = '{label1.Text}' and PayStatus = false ";
                    long startCash;
                    using (MySqlCommand command = new MySqlCommand(query, connection))
                    {
                        MySqlDataReader reader = command.ExecuteReader();//класс куда записываются наши данные
                        while (reader.Read())
                        {
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                startCash = long.Parse(reader.GetValue(i).ToString());
                                MessageBox.Show(startCash.ToString());
                                CashCounter(startCash);
                            }
                        }
                        reader.Close();
                    }
                    query = $"UPDATE pp set Sostoyanie = {Sost} WHERE  CarNumber = '{label1.Text}' and PayStatus = false ";
                    using (MySqlCommand command = new MySqlCommand(query, connection))
                    {
                        command.ExecuteReader();
                    }
                        SoundEffect();
                }
    
            }
        }

        private void button6_Click_1(object sender, EventArgs e)// выбираем фото и ищем номера (заменить на делаем скриншот камеры и потом ищем номера) (можно вообще удалить )
        {
            DialogResult res = openFileDialog2.ShowDialog();
            if (res == DialogResult.OK)
            {
                pictureBox1.Image = Image.FromFile(openFileDialog2.FileName);

                InputImage = new Mat(openFileDialog2.FileName);

                UMat um = InputImage.GetUMat(AccessType.ReadWrite);

                ProcesseImage(um);
            }
         
        }

        private string FilterOfText (string txt)
        {
            char [] chars = txt.ToCharArray();
            string tmptxt = null;
            for (int j =0 ; j < chars.Length;j++)
            {
                for (int i = 0; i < 22; i++)
                {
                    switch (i)
                    {
                        case 0:
                            {
                                if (chars[j] == 'E')
                                {
                                    tmptxt += "E";
                                }
                            }
                            break;
                        case 1:
                            {
                                if (chars[j] == 'T')
                                {
                                    tmptxt += "T";
                                }
                            }
                            break;
                        case 2:
                            {
                                if (chars[j] == 'Y')
                                {
                                    tmptxt += "Y";
                                }
                            }
                            break;
                        case 3:
                            {
                                if (chars[j] == 'O')
                                {
                                    tmptxt += "O";
                                }
                            }
                            break;
                        case 4:
                            {
                                if (chars[j] == 'P')
                                {
                                    tmptxt += "P";
                                }
                            }
                            break;
                        case 5:
                            {
                                if (chars[j] == 'A')
                                {
                                    tmptxt += "A";
                                }
                            }
                            break;
                        case 6:
                            {
                                if (chars[j] == 'H')
                                {
                                    tmptxt += "H";
                                }
                            }
                            break;
                        case 7:
                            {
                                if (chars[j] == 'K')
                                {
                                    tmptxt += "K";
                                }
                            }
                            break;
                        case 8:
                            {
                                if (chars[j] == 'X')
                                {
                                    tmptxt += "X";
                                }

                            }
                            break;
                        case 9:
                            {
                                if (chars[j] == 'C')
                                {
                                    tmptxt += "C";
                                }

                            }
                            break;
                        case 10:
                            {
                                if (chars[j] == 'B')
                                {
                                    tmptxt += "B";
                                }
                            }
                            break;
                        case 11:
                            {
                                if (chars[j] == 'M')
                                {
                                    tmptxt += "M";
                                }
                            }
                            break;
                        case 12:
                            {
                                if (chars[j] == '0')
                                {
                                    tmptxt += "0";
                                }
                            }
                            break;
                        case 13:
                            {
                                if (chars[j] == '1')
                                {
                                    tmptxt += "1";
                                }
                            }
                            break;
                        case 14:
                            {
                                if (chars[j] == '2')
                                {
                                    tmptxt += "2";
                                }
                            }
                            break;
                        case 15:
                            {
                                if (chars[j] == '3')
                                {
                                    tmptxt += "3";
                                }
                            }
                            break;
                        case 16:
                            {
                                if (chars[j] == '4')
                                {
                                    tmptxt += "4";
                                }
                            }
                            break;
                        case 17:
                            {
                                if (chars[j] == '5')
                                {
                                    tmptxt += "5";
                                }
                            }
                            break;
                        case 18:
                            {
                                if (chars[j] == '6')
                                {
                                    tmptxt += "6";
                                }
                            }
                            break;
                        case 19:
                            {
                                if (chars[j] == '7')
                                {
                                    tmptxt += "7";
                                }
                            }
                            break;
                        case 20:
                            {
                                if (chars[j] == '8')
                                {
                                    tmptxt += "8";
                                }
                            }
                            break;
                        case 21:
                            {
                                if (chars[j] == '9')
                                {
                                    tmptxt += "9";
                                }
                            }
                            break;

                    }
                }
            }
          return tmptxt  ;
        }
        

        public void SoundEffect()
        {
            player.SoundLocation = SoundName;
            player.Play();
        }

        private void toolStripComboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            CameraId = toolStripComboBox2.SelectedIndex;
        }

        private void toolStripButton1_Click(object sender, EventArgs e) //нажатие нужной кнопкм 
        {
            if (WebCams.Length == 0)
            {

            }
            else if (toolStripComboBox2.SelectedItem == null)
            {

            }
            else
            {
                Capture = new VideoCapture(CameraId);
                Capture.ImageGrabbed += Capture_ImageGrabbed;
                Capture.Start();
            }
        }

        private void Capture_ImageGrabbed(object sender, EventArgs e)
        {
            Mat m = new Mat();
            Capture.Retrieve(m);
            pictureBox1.Image  = m.ToImage<Bgr,byte>().Flip(Emgu.CV.CvEnum.FlipType.None).Bitmap; 
            timer1.Enabled = true;
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (Capture != null)
            {
                pictureBox1.Image.Save("temp",ImageFormat.Png);
                Mat m = new Mat();
                InputImage = new Mat("temp");

                UMat um = InputImage.GetUMat(AccessType.ReadWrite);

                ProcesseImage(um);
            }
        }
        
        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            connection.Close();// при закртыии форма закрываем соединение sql
        }

        private void CashCounter(long startCash)
        {
            
            long FinalyCash = long.Parse(DateTime.Now.Day.ToString()) * 24 * 60 + long.Parse(DateTime.Now.Hour.ToString()) * 60 + long.Parse(DateTime.Now.Minute.ToString());
            FinalyCash = (FinalyCash - startCash)*3;
            if (FinalyCash <0)
            {
                FinalyCash = (long.Parse(DateTime.Now.Day.ToString())+30) * 24 * 60 + long.Parse(DateTime.Now.Hour.ToString()) * 60 + long.Parse(DateTime.Now.Minute.ToString());
                FinalyCash = (FinalyCash - startCash)*3;
            }
                MessageBox.Show($"к оплате: {FinalyCash}");
        }
    }

}
