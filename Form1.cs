using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;



namespace UR2_Labs
{
    public partial class Form1 : Form
    {
        //class for shape detection and servo math
        public class Box
        {

            public Rectangle box;
            public Rectangle shape;
            public double Area = 0;
            public double centerX = 0;
            public double centerY = 0;
            public char determinShape = ' ';
            public double DistanceFromBase;
            public double Servo0 = 0;
            public double Servo1 = 0;
            public double Servo2 = 0;
            public double pixleRatio = 0;

        }
        //for opening camera
        private VideoCapture _capture;
        private Thread _captureThread;

        //Creating a serial communication ports
        SerialPort arduinoSerial = new SerialPort();

        //number initilization
        private int Threshold = 125;
        private bool run = false;



        public Form1()
        {
            InitializeComponent();

        }

        private void Form1_Load(object sender, EventArgs e)
        {
            //Opening camera
            _capture = new VideoCapture(1);
            _captureThread = new Thread(ProcessImage);
            _captureThread.Start();

            //initializing arduino
            try
            {
                arduinoSerial.PortName = "COM4";
                arduinoSerial.BaudRate = 9600;
                arduinoSerial.Open();

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error Initializing COM port");
            }
        }

        private void ProcessImage()
        {
            while (_capture.IsOpened)
            {
                //frame maintenance
                Mat sourceFrame = _capture.QueryFrame();

                // resize to PictureBox aspect ratio
                //int newHeight = (sourceFrame.Size.Height * sourcePictureBox.Size.Width) / sourceFrame.Size.Width;
                Size newSize = new Size(sourcePictureBox.Size.Height, sourcePictureBox.Width);
                CvInvoke.Resize(sourceFrame, sourceFrame, newSize);

                // display the image in the source PictureBox
                sourcePictureBox.Image = sourceFrame.Bitmap;

                // copy the source image so we can display a copy with artwork without editing the original:
                Mat sourceFrameWithArt = sourceFrame.Clone();

                // create an image version of the source frame, will be used when warping the image
                Image<Bgr, byte> sourceFrameWarped = sourceFrame.ToImage<Bgr, byte>();

                // Isolating the ROI: convert to a gray, apply binary threshold:
                Image<Gray, byte> grayImg =
                    sourceFrame.ToImage<Gray, byte>().ThresholdBinary(new Gray(125), new Gray(255));

                //resizing picture box of threshold and final images
                CvInvoke.Resize(sourceFrameWarped, sourceFrameWarped, threshPictureBox.Size);

                CvInvoke.Resize(sourceFrameWarped, sourceFrameWarped, finalPictureBox.Size);

                //Threshold Image
                Image<Gray, Byte> imgT = sourceFrameWarped.Convert<Gray, byte>();
                imgT = imgT.ThresholdBinary(new Gray(Threshold), new Gray(255));

                //Final Image
                Image<Bgr, byte> finalWarpImage = new Image<Bgr, byte>(0, 0);

                //Drawn contours Image
                Image<Bgr, Byte> imgD = finalWarpImage.Clone();

                //This is the code from the Lab using ROI and I build my code around this, credit for this part of the
                //code goes to Prof. Joe Long
                using (VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint())
                {
                    List<Box> shapes = new List<Box>();
                    // Build list of contours
                    CvInvoke.FindContours(grayImg, contours, null, RetrType.List, ChainApproxMethod.ChainApproxSimple);
                    // Selecting largest contour
                    if (contours.Size > 0)
                    {
                        double maxArea = 0;
                        int chosen = 0;
                        for (int i = 0; i < contours.Size; i++)
                        {
                            VectorOfPoint contour = contours[i];
                            shapes.Add(new Box());
                            double area = CvInvoke.ContourArea(contour);
                            if (area > maxArea)
                            {
                                maxArea = area;
                                chosen = i;
                            }
                            //Implementing bounding box for shapes to be found in
                            shapes[i].box = CvInvoke.BoundingRectangle(contours[i]);
                            shapes[i].Area = CvInvoke.ContourArea(contours[i]);

                        }


                        // Getting minimal rectangle which contains the contour
                        Rectangle boundingBox = CvInvoke.BoundingRectangle(contours[chosen]);

                        //Draw on the display frame
                        MarkDetectedObject(sourceFrameWithArt, contours[chosen], boundingBox, maxArea);

                        // Create a slightly larger bounding rectangle, we'll set it as the ROI for later warping
                        sourceFrameWarped.ROI = new Rectangle((int)Math.Min(0, boundingBox.X - 30),
                            (int)Math.Min(0, boundingBox.Y - 30),
                            (int)Math.Max(sourceFrameWarped.Width - 1, boundingBox.X + boundingBox.Width + 30),
                            (int)Math.Max(sourceFrameWarped.Height - 1, boundingBox.Y + boundingBox.Height + 30));

                        // Display the version of the source image with the added artwork, simulating ROI focus:
                        roiPictureBox.Image = sourceFrameWithArt.Bitmap;

                        // Warp the image, output it
                        finalWarpImage = WarpImage(sourceFrameWarped, contours[chosen]);

                        //if the image gets buggy, this is a saftey net to prevent a system crash by reverting to an older frame
                        if (finalWarpImage.Height == 0)
                        {
                            finalWarpImage = sourceFrame.ToImage<Bgr, byte>();
                        }

                        //Setting image to final picture box
                        imgD = finalWarpImage.Clone();
                        warpedPictureBox.Image = finalWarpImage.Bitmap;
                    }
                }

                if (finalWarpImage.Height > 0)
                {
                    //Thresholding image and having the final image read from the threshold image to do the line detection
                    var temp = finalWarpImage
                        .SmoothGaussian(5)
                        .Convert<Gray, byte>()
                        .ThresholdBinary(new Gray(Threshold), new Gray(255));

                    //initalizing vector counting
                    VectorOfVectorOfPoint contours1 = new VectorOfVectorOfPoint();
                    Mat m = new Mat();

                    CvInvoke.FindContours(temp, contours1, m, Emgu.CV.CvEnum.RetrType.Ccomp,
                    Emgu.CV.CvEnum.ChainApproxMethod.ChainApproxSimple);

                    //starting new list for class
                    List<Box> shapes = new List<Box>();
                    double maxArea = 0;
                    int chosen = 0;
                    bool pickedShape = false;
                    int chosenShape = 0;

                    //for loop to determine shape location, type, and draw on picture box
                    for (int i = 0; i < contours1.Size; i++)
                    {

                        double perimeter = CvInvoke.ArcLength(contours1[i], true);
                        VectorOfPoint approx = new VectorOfPoint();

                        CvInvoke.ApproxPolyDP(contours1[i], approx, 0.04 * perimeter, true);

                        //Drawing Contours


                        //center point for drawing so show T or S in picture Box
                        var moments = CvInvoke.Moments(contours1[i]);
                        int x = (int)(moments.M10 / moments.M00);
                        int y = (int)(moments.M01 / moments.M00);

                        //shape centerpoint determination
                        var shape = new Box()
                        {
                            centerY = (moments.M01 / moments.M00),
                            centerX = (moments.M10 / moments.M00),

                        };
                        shapes.Add(shape);
                        shapes[i].shape = CvInvoke.BoundingRectangle(contours1[i]);
                        Rectangle ratio = CvInvoke.BoundingRectangle(contours1[chosen]);
                        shapes[chosen].pixleRatio = 8.5 / ratio.Height;


                        //picking shape for later logic
                        if (pickedShape == false)
                        {
                            chosenShape = i;
                            pickedShape = true;
                        }

                        //triangle determination and drawing
                        if (approx.Size == 3)
                        {
                            double area = CvInvoke.ContourArea(approx, false);
                            if (area < (.5 * finalPictureBox.Width * finalPictureBox.Height))
                            {
                                CvInvoke.DrawContours(imgD, contours1, i, new MCvScalar(0, 0, 255), 2);
                                CvInvoke.PutText(imgD, "T", new Point(x, y),
                                    Emgu.CV.CvEnum.FontFace.HersheySimplex, 0.5, new MCvScalar(0, 0, 255), 2);
                                shapes[i].determinShape = 'T';
                                //triangleCount += 1;
                            }
                        }

                        //Rectangle determination and drawing
                        if (approx.Size == 4)
                        {

                            Rectangle rect = CvInvoke.BoundingRectangle(contours1[i]);
                            double area = CvInvoke.ContourArea(approx, false);
                            if (area < (.5 * finalPictureBox.Width * finalPictureBox.Height))
                            {
                                CvInvoke.DrawContours(imgD, contours1, i, new MCvScalar(255, 0, 0), 2);
                                CvInvoke.PutText(imgD, "S", new Point(x, y),
                                    Emgu.CV.CvEnum.FontFace.HersheySimplex, 0.5, new MCvScalar(255, 0, 0), 2);
                                shapes[i].determinShape = 'S';
                                //rectangleCount += 1;
                            }

                        }

                    }
                    //Sending bounding box to Find Values function
                    for (int i = 0; i < shapes.Count; i++)
                    {
                        if (i != chosen)
                        {
                            Rectangle BoundingRect = CvInvoke.BoundingRectangle(contours1[i]);
                            Find_Values(BoundingRect, shapes[i], shapes[chosen]);

                        }
                    }
                    //Sending data to arduino via serial communication
                    if (run == true)
                    {
                        //Send Data to Arduino if shapes are found
                        if (chosenShape != -1)
                        {
                            //Converting class servo integers into a local integer to send to the arduino
                            int Servo0 = (int)(shapes[chosenShape].Servo0);
                            int Servo1 = (int)(shapes[chosenShape].Servo1);
                            int Servo2 = (int)(shapes[chosenShape].Servo2);

                            string data = "<" + Servo0 + " " + Servo1 + " " + Servo2 + ">";
                            arduinoSerial.Write(data);




                        }
                    }
                    //drawing image
                    threshPictureBox.Image = temp.Bitmap;
                    finalPictureBox.Image = imgD.Bitmap;
                }
            }
        }

        //Warp image code also from UR2 Lab, credit to Joe Long
        private static Image<Bgr, Byte> WarpImage(Image<Bgr, byte> frame, VectorOfPoint contour)
        {
            // set the output size:
            var size = new Size(frame.Width, frame.Height);
            using (VectorOfPoint approxContour = new VectorOfPoint())
            {
                CvInvoke.ApproxPolyDP(contour, approxContour, CvInvoke.ArcLength(contour, true) * 0.05, true);
                // get an array of points in the contour
                Point[] points = approxContour.ToArray();
                // if array length isn't 4, something went wrong, abort warping process (for demo, draw points instead)
                if (points.Length != 4)
                {
                    for (int i = 0; i < points.Length; i++)
                    {
                        frame.Draw(new CircleF(points[i], 5), new Bgr(Color.Red), 5);
                    }

                    return frame;
                }

                IEnumerable<Point> query = points.OrderBy(point => point.Y).ThenBy(point => point.X);
                PointF[] ptsSrc = new PointF[4];
                PointF[] ptsDst = new PointF[]
                {
                    new PointF(0, 0), new PointF(size.Width - 1, 0), new PointF(0, size.Height - 1),
                    new PointF(size.Width - 1, size.Height - 1)
                };
                for (int i = 0; i < 4; i++)
                {
                    ptsSrc[i] = new PointF(query.ElementAt(i).X, query.ElementAt(i).Y);
                }

                using (var matrix = CvInvoke.GetPerspectiveTransform(ptsSrc, ptsDst))
                {
                    using (var cutImagePortion = new Mat())
                    {
                        CvInvoke.WarpPerspective(frame, cutImagePortion, matrix, size, Inter.Cubic);
                        return cutImagePortion.ToImage<Bgr, Byte>();
                    }
                }
            }
        }

        private static void MarkDetectedObject(Mat frame, VectorOfPoint contour, Rectangle boundingBox, double area)
        {
            // Drawing contour and box around it
            CvInvoke.Polylines(frame, contour, true, new Bgr(Color.Red).MCvScalar);
            CvInvoke.Rectangle(frame, boundingBox, new Bgr(Color.Red).MCvScalar);
            // Write information next to marked object
            Point center = new Point(boundingBox.X + boundingBox.Width / 2, boundingBox.Y + boundingBox.Height / 2);
            var info = new string[]
            {
                $"Area: {area}",
                $"Position: {center.X}, {center.Y}"
            };
            WriteMultilineText(frame, info, new Point(center.X, boundingBox.Bottom + 12));
        }

        private static void WriteMultilineText(Mat frame, string[] lines, Point origin)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                int y = i * 10 + origin.Y;
                // Moving down on each line
                CvInvoke.PutText(frame, lines[i], new Point(origin.X, y),
                    FontFace.HersheyPlain, 0.8, new Bgr(Color.Red).MCvScalar);
            }
        }

        //Find values for the servos
        private void Find_Values(Rectangle boundingRec, Box shape, Box box)
        {


            double disX = (box.centerX - shape.centerX) * box.pixleRatio;
            double disY = (shape.centerY) * box.pixleRatio + 7.75;
            box.Servo0 = Math.Round((Math.Atan(disX / disY) * (180 / Math.PI) + 90));
            box.DistanceFromBase = Math.Sqrt(Math.Pow(disY, 2) + Math.Pow(disX, 2));

            /*
            Calculating the 180 degree servos angles using the law of cosines to determine what angles the servos need to be
            at considering the lengths of the arms. This allows the robot to move to where it needs to be to pick up
            the shape 


                 /C\
              b /   \ a
               /     \ 
              /A     B\
              --------
                  c
            */
            double a = 8; // link 2 robot arm "a"
            double b = 7.5; // link 1 robot arm "b"
            double c = box.DistanceFromBase; // solved distance "c"
            box.Servo1 = Math.Round((Math.Acos((Math.Pow(b, 2) + Math.Pow(c, 2) - (Math.Pow(a, 2))) / (2 * b * c)) * 180 / Math.PI)); //Angle "A"
            box.Servo2 = Math.Round((Math.Acos((Math.Pow(a, 2) + Math.Pow(b, 2) - (Math.Pow(c, 2))) / (2 * c * a)) * 180 / Math.PI)); //Angle "B"
            Invoke(new Action(() =>
            {
                label3.Text = Math.Round(box.Servo0, 2).ToString();
                label4.Text = box.Servo1.ToString();
                label5.Text = box.Servo2.ToString();
            }));
        }

        //closing the thread
        private void form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _captureThread.Abort();
        }

        //threshold scrollbar
        private void trackBar1_Scroll(object sender, EventArgs e)
        {
            Threshold = trackBar1.Value;
            label1.Text = Threshold.ToString();
        }

        //run program
        private void buttonRun_Click_1(object sender, EventArgs e)
        {
            if (run == false)
            {
                run = true;
            }

            if (run == true)
            {
                run = false;
            }
        }
    }


}