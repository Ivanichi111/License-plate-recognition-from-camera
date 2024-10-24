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

using Emgu;
using Emgu.CV;
using Emgu.CV.Util;
using Emgu.CV.Structure;
using Emgu.CV.OCR;
using Emgu.Util;
using Emgu.CV.CvEnum;
using Emgu.CV.Stitching;
using System.Reflection.Emit;

namespace PayParking
{
    internal class NumberOfCar: DisposableObject
    {
        private Tesseract ocr;

        public NumberOfCar ()
        {
            ocr = new Tesseract("","eng", OcrEngineMode.TesseractLstmCombined);// переместить файл в папку темп и изменить путь на название файла 
        }

        public List<string> DetectLicensePlate(IInputArray image, List<IInputOutputArray> licensePlateImageList, List<IInputOutputArray> filteredLicensePlateRegionList, List<RotatedRect> detectedLicensePlateRegionList)
        {
            List<string> licenses = new List<string>();

            using (Mat gray = new Mat())
            {
                using (Mat canny = new Mat())
                {
                    using (VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint())
                    {
                        CvInvoke.CvtColor(image,gray,ColorConversion.Bgr2Gray);

                        CvInvoke.Canny(gray, canny, 100, 50, 3, false);

                        int[,] hierarhy = CvInvoke.FindContourTree(canny,contours,ChainApproxMethod.ChainApproxSimple);

                        FindLicensePlate(contours, hierarhy, 0, gray, canny, licensePlateImageList, filteredLicensePlateRegionList, detectedLicensePlateRegionList, licenses);

                    }
                }
            }
            return licenses;
        }
        //где-то тут надо править чтоб отображал и регион 
      
        private void FindLicensePlate(VectorOfVectorOfPoint contours, int[,] hierarhy, int index, IInputArray gray, IInputArray canny, List<IInputOutputArray> licensePlateImageList, List<IInputOutputArray> filteredLicensePlateRegionList, List<RotatedRect> detectedLicensePlateRegionList, List<string> licenses)
        {
            for (;index >=0;index = hierarhy[index,0])
            {
                int numberOfChildren = GetNumberOfChildren (hierarhy, index);
                if (numberOfChildren == 0)
                {
                    continue;
                }

                using (VectorOfPoint contur = contours[index])
                {
                    if (CvInvoke.ContourArea(contur)>400) //мб тут 
                    {
                        if (numberOfChildren < 1)// мб тут
                        {
                            FindLicensePlate(contours, hierarhy, hierarhy[index,2],gray,canny, licensePlateImageList, filteredLicensePlateRegionList, detectedLicensePlateRegionList, licenses);
                            continue;
                        }
                        RotatedRect box = CvInvoke.MinAreaRect(contur);
                        if (box.Angle < -45.0)
                        {
                            float tmp = box.Size.Width;// mb tut
                            box.Size.Width = box.Size.Height;
                            box.Size.Height = tmp;
                            box.Angle += 90f;
                        }
                        else if (box.Angle > 45.0)
                        {
                            float tmp = box.Size.Width;
                            box.Size.Width = box.Size.Height;
                            box.Size.Height = tmp;

                            box.Angle -= 90f;
                        }

                        double whRatio = (double)box.Size.Width / box.Size.Height;

                        if (!(3.0 < whRatio &&  whRatio < 10.0))
                        {
                            if (hierarhy[index,2] > 0)
                            {
                                FindLicensePlate(contours, hierarhy, hierarhy[index, 2], gray, canny, licensePlateImageList, filteredLicensePlateRegionList, detectedLicensePlateRegionList, licenses);
                                continue;
                            }
                        }

                        using (UMat tmp1 = new UMat())
                        {
                            using (UMat tmp2 = new UMat())
                            {
                                PointF[] srcCorners = box.GetVertices();

                                PointF[] dstCorners = new PointF[]
                                {
                                    new PointF(0,box.Size.Height-1),
                                    new PointF (0,0),
                                    new PointF(box.Size.Width-1,0),
                                    new PointF (box.Size.Width-1,box.Size.Height-1)
                                };

                                using (Mat rot = CvInvoke.GetAffineTransform(srcCorners,dstCorners))
                                {
                                    CvInvoke.WarpAffine(gray, tmp1, rot, Size.Round(box.Size));
                                }

                                Size approxSize = new Size(240, 180); // размер обрабатываемой области 

                                double scale = Math.Min(approxSize.Width / box.Size.Width, approxSize.Height / box.Size.Height);

                                Size newSize = new Size((int)Math.Round(box.Size.Width * scale), (int)Math.Round(box.Size.Height * scale));

                                CvInvoke.Resize (tmp1,tmp2 , newSize,0,0, Inter.Cubic);

                                //отсюда идёт удаление угловых пикселей 

                                int edgePixelSize = 2;

                                Rectangle newRoi = new Rectangle(new Point(edgePixelSize, edgePixelSize), tmp2.Size  - new Size(2 * edgePixelSize ,2 * edgePixelSize));

                                UMat plate = new UMat(tmp2,newRoi);

                                UMat filterdPlate = FilterPlate (plate);

                                StringBuilder stringBuilder = new StringBuilder();

                                using (UMat tmp = filterdPlate.Clone())
                                {
                                    ocr.SetImage(tmp);
                                    ocr.Recognize();
                                    stringBuilder.AppendLine(ocr.GetUTF8Text());
                                }
                                licenses.Add(stringBuilder.ToString());

                                licensePlateImageList.Add(plate);

                                filteredLicensePlateRegionList.Add(filterdPlate);

                                detectedLicensePlateRegionList.Add(box);
                            }
                        }
                    }
                }
            }
        }
    
        private int GetNumberOfChildren(int[,] hierarhy,int index)
        {
            index = hierarhy[index, 2];

            if (index < 0)
            {
                return 0;
            }
            int count = 1;
            while (hierarhy[index, 0] > 0)
            {
                count++;

                index = hierarhy[index, 0];
            }
            return count;
        }


        private static UMat FilterPlate(UMat plate)
        {
            UMat thresh = new UMat();

            CvInvoke.Threshold(plate, thresh, 120, 255, ThresholdType.BinaryInv);
            Size platesize = plate.Size;

            using (Mat platMask = new Mat(platesize.Height,platesize.Width,DepthType.Cv8U,1)) 
            {
                using (Mat plateCanny = new Mat())
                {
                    using (VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint())
                    {
                        platMask.SetTo(new MCvScalar(255.0));
                        CvInvoke.Canny(plate, plateCanny, 100, 50);

                        CvInvoke.FindContours(plateCanny, contours, null, RetrType.External, ChainApproxMethod.ChainApproxSimple);
                        int count = contours.Size;
                        
                        for (int i = 0; i < count; i++)
                        {
                            using (VectorOfPoint contour = contours[i])
                            {
                                Rectangle rect = CvInvoke.BoundingRectangle(contour);

                                if (rect.Height > (platesize.Height>>1))
                                {
                                    rect.X -= 1;
                                    rect.Y -= 1;

                                    rect.Width += 2;
                                    rect.Height += 2;

                                    Rectangle roi = new Rectangle(Point.Empty, plate.Size);
                                    rect.Intersect(roi);
                                    CvInvoke.Rectangle(platMask, rect, new MCvScalar(), -1);
                                }
                            }
                        }

                        thresh.SetTo(new MCvScalar(),platMask);
                    }
                }

            }
            CvInvoke.Erode(thresh, thresh, null, new Point(-1, -1),1,BorderType.Constant,CvInvoke.MorphologyDefaultBorderValue);

            CvInvoke.Dilate(thresh, thresh, null, new Point(-1, -1), 1, BorderType.Constant, CvInvoke.MorphologyDefaultBorderValue);

            return thresh;
        }

        protected override void DisposeObject()
        {
            ocr.Dispose();
        }
    }
}
