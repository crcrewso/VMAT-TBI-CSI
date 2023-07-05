﻿using System;
using System.IO;
using System.Drawing.Imaging;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Collections.Generic;
using System.Linq;
using EvilDICOM.Core;
using EvilDICOM.Core.Helpers;
using EvilDICOM.Network;
using EvilDICOM.Network.Enums;
using SimpleProgressWindow;
using VMATTBICSIAutoPlanningHelpers.Enums;
using VMATTBICSIAutoPlanningHelpers.Structs;

namespace VMATTBICSIAutoPlanningHelpers.Helpers
{
    public class CTImageExport : SimpleMTbase
    {
        //overridden during construction of classx
        private string _LocalAET;                 // local AE title
        private string _AriaDBAET;               // AE title of VMS DB Daemon
        private string _VMSFileAET;                 // AE title of VMS File Daemon
        private string _AriaDBIP;                  // IP address of server hosting the DB Daemon
        private int _AriaDBPort;                //port daemon is listening to
        private string _VMSFileIP;                  // IP address of server hosting the DB Daemon
        private int _VMSFIlePort;                //port daemon is listening to
        private int _LocalPort;

        private ImgExportFormat _exportFormat;
        private VMS.TPS.Common.Model.API.Image _image;
        private string _writeLocation;
        private string _importLocation;
        private string _patID;

        private ImportExportStruct _data;

        public CTImageExport(VMS.TPS.Common.Model.API.Image img, 
                             string imgWriteLocation,
                             string importPath,
                             string patientID,
                             ImgExportFormat exportFormat, 
                             string AriaDBAETitle = @"VMSDBD", 
                             string AriaDBIP = "10.151.176.60",
                             int AriaDBPort = 51402,
                             string VMSFileAETitle = @"VMSFD", 
                             string VMSFileIP = "10.151.176.60",
                             int VMSFilePort = 51402,
                             string localAETitle = @"DCMTK",
                             int localPort = 50400,
                             ImgExportFormat theData) 
        {
            _AriaDBAET = AriaDBAETitle;
            _AriaDBIP = AriaDBIP;
            _AriaDBPort = AriaDBPort;
            _VMSFileAET = VMSFileAETitle;
            _VMSFileIP = VMSFileIP;
            _VMSFIlePort = VMSFilePort;
            _LocalAET = localAETitle;
            _LocalPort = localPort;
            _exportFormat = exportFormat;
            _image = img;
            _writeLocation = imgWriteLocation;
            _importLocation = importPath;
            _patID = patientID;
        }

        public override bool Run()
        {
            bool result = false;
            if (_exportFormat == ImgExportFormat.PNG) result = ExportAsPNG();
            else if (_exportFormat == ImgExportFormat.DICOM) result = ExportAsDCM();
            if (!result)
            {
                UpdateUILabel("Finished:");
                ProvideUIUpdate($"{_image.Id} has been exported successfully!");
            }
            return result;
        }

        #region PNG export
        private bool ExportAsPNG()
        {
            UpdateUILabel("Exporting as PNG:");
            int percentComplete = 0;
            int calcItems = 1 + _image.ZSize;
            ProvideUIUpdate("Initializing...");
            int[,] pixels = new int[_image.XSize, _image.YSize];
            //write 16 bit-depth image (still having problems as of 7/20/2022. Geometry and image looks vaguely correct, but the gray levels look off)
            //try
            //{
            //    for (int k = 0; k < img.ZSize; k++)
            //    {
            //        img.GetVoxels(k, pixels);
            //        FromTwoDimIntArrayGray(pixels, k, imgWriteLocation);
            //        //SaveBmp(bmp, imgWriteLocation);
            //    }
            //    return false;
            //}
            //catch (Exception e) { MessageBox.Show(e.StackTrace); return true; }

            //the main limitation of this method is the maximum bit depth is limited to 8 (whereas CT data has a bit depth of 12)
            try
            {
                string ct_ID = _image.Id;
                string folderLoc = Path.Combine(_writeLocation, _patID);
                if (!Directory.Exists(folderLoc)) Directory.CreateDirectory(folderLoc);
                ProvideUIUpdate((int)(100 * ++percentComplete / calcItems), "Initializtion complete. Exporting");
                for (int k = 0; k < _image.ZSize; k++)
                {
                    ProvideUIUpdate((int)(100 * ++percentComplete / calcItems));
                    Bitmap bmp = new Bitmap(_image.XSize, _image.YSize, PixelFormat.Format32bppRgb);
                    _image.GetVoxels(k, pixels);
                    int i, j;
                    for (j = 0; j < _image.YSize; j++)
                    {
                        for (i = 0; i < _image.XSize; i++)
                        {
                            int r, b, g;
                            int val = (int)((double)pixels[i, j] / Math.Pow(2, 12) * 255);
                            if (val > 255) r = b = g = 255;
                            else r = b = g = val;
                            bmp.SetPixel(i, j, Color.FromArgb(255, r, g, b));
                        }
                    }
                    bmp.Save(String.Format(@"{0}\{1}_{2}.png", folderLoc, ct_ID, k));
                }
            }
            catch (Exception e) 
            { 
                ProvideUIUpdate(e.Message, true); 
                return true; 
            }
            UpdateUILabel("Finished!");
            return false;
        }

        private void FromTwoDimIntArrayGray(Int32[,] data, int sliceNum, string writeLocation)
        {
            // Transform 2-dimensional Int32 array to 1-byte-per-pixel byte array
            Int32 width = data.GetLength(0);
            Int32 height = data.GetLength(1);
            //stride must be a multiple of 4
            Int32 stride = 4 * ((width * 2 + 3) / 4);
            Int32 byteIndex = 0;
            byte[] dataBytes = new byte[height * stride];
            for (Int32 y = 0; y < height; y++)
            {
                for (Int32 x = 0; x < width; x++)
                {
                    // read the bytes and shift right. Shifting bits right does NOT alter value.
                    // NOTE: signed int is BIG ENDIAN (unsigned ints are little endian)
                    //dataBytes[byteIndex] = (Byte)(val >> 24);
                    //dataBytes[byteIndex + 1] = (Byte)(val >> 16);
                    dataBytes[byteIndex] = (Byte)(data[x, y] >> 08);
                    dataBytes[byteIndex + 1] = (Byte)data[x, y];

                    // More efficient than multiplying
                    byteIndex += 2;

                }
            }
            //MemoryStream ms = new MemoryStream(dataBytes);
            //ms.Seek(0, SeekOrigin.Begin);
            //create new bitmap of width and hight equal to width and height of axial CT image
            Bitmap bmp = new Bitmap(width, height);
            //lock the bits (pay attention to pixel format argument)
            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format16bppRgb565);
            //copy the byte array determined from the loop above to the bitmap
            Marshal.Copy(dataBytes, 0, bmpData.Scan0, dataBytes.Length);

            BitmapSource source = BitmapSource.Create(bmp.Width,
                                                      bmp.Height,
                                                      96,
                                                      96,
                                                      System.Windows.Media.PixelFormats.Gray16,
                                                      null,
                                                      bmpData.Scan0,
                                                      bmpData.Stride * bmp.Height,
                                                      bmpData.Stride);
            bmp.UnlockBits(bmpData);
            FileStream stream = new FileStream(Path.Combine(writeLocation, String.Format("{0}.bmp", sliceNum)), FileMode.Create);

            TiffBitmapEncoder encoder = new TiffBitmapEncoder();

            encoder.Compression = TiffCompressOption.Zip;
            encoder.Frames.Add(BitmapFrame.Create(source));
            encoder.Save(stream);

            stream.Close();
        }

        //private void SaveBmp(Bitmap bmp, string path)
        //{
        //    Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);

        //    BitmapData bitmapData = bmp.LockBits(rect, ImageLockMode.ReadOnly, bmp.PixelFormat);

        //    System.Windows.Media.PixelFormat pixelFormats = ConvertBmpPixelFormat(bmp.PixelFormat);

        //    BitmapSource source = BitmapSource.Create(bmp.Width,
        //                                              bmp.Height,
        //                                              bmp.HorizontalResolution,
        //                                              bmp.VerticalResolution,
        //                                              pixelFormats,
        //                                              null,
        //                                              bitmapData.Scan0,
        //                                              bitmapData.Stride * bmp.Height,
        //                                              bitmapData.Stride);

        //    bmp.UnlockBits(bitmapData);


        //    FileStream stream = new FileStream(Path.Combine(path,"testing.bmp"), FileMode.Create);

        //    TiffBitmapEncoder encoder = new TiffBitmapEncoder();

        //    encoder.Compression = TiffCompressOption.Zip;
        //    encoder.Frames.Add(BitmapFrame.Create(source));
        //    encoder.Save(stream);

        //    stream.Close();
        //}

        //private static System.Windows.Media.PixelFormat ConvertBmpPixelFormat(System.Drawing.Imaging.PixelFormat pixelformat)
        //{
        //    //System.Windows.Media.PixelFormat pixelFormats = System.Windows.Media.PixelFormats.Default;

        //    //switch (pixelformat)
        //    //{
        //    //    case System.Drawing.Imaging.PixelFormat.Format32bppArgb:
        //    //        pixelFormats = PixelFormats.Bgr32;
        //    //        break;

        //    //    case System.Drawing.Imaging.PixelFormat.Format8bppIndexed:
        //    //        pixelFormats = PixelFormats.Gray8;
        //    //        break;

        //    //    case System.Drawing.Imaging.PixelFormat.Format16bppGrayScale:
        //    //        pixelFormats = PixelFormats.Gray16;
        //    //        break;
        //    //}

        //    return PixelFormats.Gray32Float;
        //}
        #endregion

        #region DCM export
        private bool ExportAsDCM()
        {
            Entity ariaDBDaemon = new Entity(_AriaDBAET, _AriaDBIP, _AriaDBPort);
            Entity VMSFileDaemon = new Entity(_VMSFileAET, _VMSFileIP, _VMSFIlePort);
            Entity localDaemon = Entity.CreateLocal(_LocalAET, _LocalPort);
            if (PreliminaryChecks(ariaDBDaemon, localDaemon)) return true;
            UpdateUILabel("Exporting Dicom Images:");

            var client = new DICOMSCU(localDaemon);
            var receiver = new DICOMSCP(localDaemon)
            {
                SupportedAbstractSyntaxes = AbstractSyntax.ALL_RADIOTHERAPY_STORAGE,
            };
            receiver.DIMSEService.CStoreService.CStorePayloadAction = (dcm, asc) =>
            {
                var path = Path.Combine(_writeLocation, dcm.GetSelector().SOPInstanceUID.Data + ".dcm");
                ProvideUIUpdate($"Writing file {dcm.GetSelector().SOPInstanceUID.Data + ".dcm"}");
                dcm.Write(path);
                return true;
            };
            receiver.ListenForIncomingAssociations(true);

            EvilDICOM.Network.SCUOps.CFinder finder = client.GetCFinder(ariaDBDaemon);
            List<EvilDICOM.Network.DIMSE.IOD.CFindStudyIOD> studies = finder.FindStudies(_patID).ToList();
            List<EvilDICOM.Network.DIMSE.IOD.CFindSeriesIOD> series = finder.FindSeries(studies).ToList();
            ProvideUIUpdate($"Found {series.Count()} series for {_patID}");
            ProvideUIUpdate($"Found {series.Where(x => x.Modality == "CT").Count()} total CT images for {_patID}");

            if (series.Any(x => string.Equals(x.SeriesInstanceUID, _image.Series.UID)))
            {
                ProvideUIUpdate($"Found matching series for {_image.Id} based on UID: {_image.Series.UID}");
                EvilDICOM.Network.DIMSE.IOD.CFindSeriesIOD ctSeries = series.First(x => x.SeriesInstanceUID == _image.Series.UID);
                List<EvilDICOM.Network.DIMSE.IOD.CFindInstanceIOD> imageStack = finder.FindImages(ctSeries).ToList();
                ProvideUIUpdate($"Total CT slices for export: {imageStack.Count()}");

                var mover = client.GetCMover(ariaDBDaemon);
                ushort msgId = 1;
                int numImages = imageStack.Count();
                int counter = 0;
                foreach (var img in imageStack)
                {
                    ProvideUIUpdate((int)(100 * ++counter / numImages), $"Exporting image: {img.SOPInstanceUID}");
                    EvilDICOM.Network.DIMSE.CMoveResponse response = mover.SendCMove(img, VMSFileDaemon.AeTitle, ref msgId);
                    if ((Status)response.Status != Status.SUCCESS)
                    {
                        CMoveFailed(response);
                        return true;
                    }
                }
            }
            else
            {
                ProvideUIUpdate($"Error! Matching image series not found for UID: {_image.Series.UID}! Exiting!", true);
                return true;
            }
            return false;
        }

        private void CMoveFailed(EvilDICOM.Network.DIMSE.CMoveResponse response)
        {
            ProvideUIUpdate($"Error! C-Move operation failed!");
            ProvideUIUpdate("DICOM C-Move Results:");
            ProvideUIUpdate($"Completed moves: {response.NumberOfCompletedOps}");
            ProvideUIUpdate($"Failed moves: {response.NumberOfFailedOps}");
            ProvideUIUpdate($"Warning moves: {response.NumberOfWarningOps}");
            ProvideUIUpdate($"Remaining moves: {response.NumberOfRemainingOps}");
            ProvideUIUpdate("Exiting!", true);
        }

        private bool PreliminaryChecks(Entity aria, Entity local)
        {
            UpdateUILabel("Preliminary Checks:");
            if (PingDaemon(aria, local)) return true;
            if (!Directory.Exists(_writeLocation))
            {
                ProvideUIUpdate($"Error! {_writeLocation} does not exist! Exiting!", true);
                return true;
            }
            return false;
        }

        private bool PingDaemon(Entity daemon, Entity local)
        {
            ProvideUIUpdate($"C Echo from {local.AeTitle} => {daemon.AeTitle} @ {daemon.IpAddress} : {daemon.Port}");
            DICOMSCU client = new DICOMSCU(local);
            //5 sec timeout
            bool success = client.Ping(daemon, 5000);
            ProvideUIUpdate($"Success: {success}", !success);
            return !success;
        }

        public bool ImportRTStructureSet()
        {
            Entity ariaDBDaemon = new Entity(_AriaDBAET, _AriaDBIP, _AriaDBPort);
            Entity localDaemon = Entity.CreateLocal(_LocalAET, _LocalPort);
            if (PreliminaryChecks(ariaDBDaemon, localDaemon)) return true;
            UpdateUILabel("Import RT Structure Set:");

            var client = new DICOMSCU(localDaemon);
            var storer = client.GetCStorer(ariaDBDaemon);
            ushort msgId = 1;
            var dcmFiles = Directory.GetFiles(_importLocation);
            foreach (var path in dcmFiles)
            {
                var dcm = DICOMObject.Read(path);
                var response = storer.SendCStore(dcm, ref msgId);
                if((Status)response.Status != Status.SUCCESS)
                {
                    ProvideUIUpdate($"CStore failed", true);
                }
                else
                {
                    ProvideUIUpdate($"DICOM C-Store from {localDaemon.AeTitle} => {ariaDBDaemon.AeTitle} @{ariaDBDaemon.IpAddress}:{ariaDBDaemon.Port}: {(Status)response.Status}");
                }
            }
            return false;
        }
        #endregion
    }
}
