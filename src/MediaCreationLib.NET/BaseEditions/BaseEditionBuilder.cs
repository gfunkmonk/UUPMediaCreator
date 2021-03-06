﻿using Imaging;
using MediaCreationLib.NET;
using Microsoft.Cabinet;
using Microsoft.Wim;
using System;
using System.Collections.Generic;
using System.IO;
using UUPMediaCreator.InterCommunication;
using static MediaCreationLib.MediaCreator;

namespace MediaCreationLib.BaseEditions
{
    public class BaseEditionBuilder
    {
        private static WIMImaging imagingInterface = new WIMImaging();

        public static bool CreateBaseEdition(
            string UUPPath,
            string LanguageCode,
            string EditionID,
            string InputWindowsREPath,
            string OutputInstallImage,
            Common.CompressionType CompressionType,
            ProgressCallback progressCallback = null)
        {
            bool result = true;

            WimCompressionType compression = WimCompressionType.None;
            switch (CompressionType)
            {
                case Common.CompressionType.LZMS:
                    compression = WimCompressionType.Lzms;
                    break;

                case Common.CompressionType.LZX:
                    compression = WimCompressionType.Lzx;
                    break;

                case Common.CompressionType.XPRESS:
                    compression = WimCompressionType.Xpress;
                    break;
            }

            HashSet<string> ReferencePackages, referencePackagesToConvert;
            string BaseESD = null;

            (result, BaseESD, ReferencePackages, referencePackagesToConvert) = FileLocator.LocateFilesForBaseEditionCreation(UUPPath, LanguageCode, EditionID, progressCallback);
            if (!result)
                goto exit;

            progressCallback?.Invoke(Common.ProcessPhase.PreparingFiles, true, 0, "Converting Reference Cabinets");

            int counter = 0;
            int total = referencePackagesToConvert.Count;
            foreach (var file in referencePackagesToConvert)
            {
                int progressoffset = (int)Math.Round((double)counter / total * 100);
                int progressScale = (int)Math.Round((double)1 / total * 100);

                string refesd = ConvertCABToESD(Path.Combine(UUPPath, file), progressCallback, progressoffset, progressScale);
                if (string.IsNullOrEmpty(refesd))
                {
                    progressCallback?.Invoke(Common.ProcessPhase.ReadingMetadata, true, 0, "Reference ESD creation from Cabinet files failed");
                    goto exit;
                }
                ReferencePackages.Add(refesd);
                counter++;
            }

            //
            // Gather information to transplant later into DisplayName and DisplayDescription
            //
            WIMInformationXML.IMAGE image;
            imagingInterface.GetWIMImageInformation(BaseESD, 3, out image);

            //
            // Export the install image
            //
            void callback(string Operation, int ProgressPercentage, bool IsIndeterminate)
            {
                progressCallback?.Invoke(Common.ProcessPhase.ApplyingImage, IsIndeterminate, ProgressPercentage, Operation);
            };

            result = imagingInterface.ExportImage(
                BaseESD,
                OutputInstallImage,
                3,
                referenceWIMs: ReferencePackages,
                compressionType: compression,
                progressCallback: callback);
            if (!result)
                goto exit;

            WIMInformationXML.WIM wim;
            imagingInterface.GetWIMInformation(OutputInstallImage, out wim);

            //
            // Set the correct metadata on the image
            //
            image.DISPLAYNAME = image.NAME;
            image.DISPLAYDESCRIPTION = image.NAME;
            image.FLAGS = image.WINDOWS.EDITIONID;
            if (image.WINDOWS.LANGUAGES == null)
            {
                image.WINDOWS.LANGUAGES = new WIMInformationXML.LANGUAGES()
                {
                    LANGUAGE = LanguageCode,
                    FALLBACK = new WIMInformationXML.FALLBACK()
                    {
                        LANGUAGE = LanguageCode,
                        Text = "en-US"
                    },
                    DEFAULT = LanguageCode
                };
            }
            result = imagingInterface.SetWIMImageInformation(OutputInstallImage, wim.IMAGE.Count, image);
            if (!result)
                goto exit;

            void callback2(string Operation, int ProgressPercentage, bool IsIndeterminate)
            {
                progressCallback?.Invoke(Common.ProcessPhase.IntegratingWinRE, IsIndeterminate, ProgressPercentage, Operation);
            };

            //
            // Integrate the WinRE image into the installation image
            //
            progressCallback?.Invoke(Common.ProcessPhase.IntegratingWinRE, true, 0, "");

            result = imagingInterface.AddFileToImage(
                OutputInstallImage,
                wim.IMAGE.Count,
                InputWindowsREPath,
                Path.Combine("Windows", "System32", "Recovery", "Winre.wim"),
                callback2);
            if (!result)
                goto exit;

            exit:
            return result;
        }

        private static string ConvertCABToESD(string cabFilePath, ProgressCallback progressCallback, int progressoffset, int progressscale)
        {
            string esdFilePath = Path.ChangeExtension(cabFilePath, "esd");
            if (File.Exists(esdFilePath))
                return esdFilePath;

            progressCallback?.Invoke(Common.ProcessPhase.PreparingFiles, false, progressoffset, "Unpacking...");

            var tmp = Path.GetTempFileName();
            File.Delete(tmp);

            string tempExtractionPath = Path.Combine(tmp, "Package");
            int progressScaleHalf = progressscale / 2;

            void ProgressCallback(int percent, string file)
            {
                progressCallback?.Invoke(Common.ProcessPhase.PreparingFiles, false, progressoffset + (int)Math.Round((double)percent / 100 * progressScaleHalf), "Unpacking " + file + "...");
            };

            CabinetHandler.ExpandFiles(cabFilePath, tempExtractionPath, ProgressCallback);

            void callback(string Operation, int ProgressPercentage, bool IsIndeterminate)
            {
                progressCallback?.Invoke(Common.ProcessPhase.PreparingFiles, IsIndeterminate, progressoffset + progressScaleHalf + (int)Math.Round((double)ProgressPercentage / 100 * progressScaleHalf), Operation);
            };

            bool result = imagingInterface.CaptureImage(esdFilePath, "Metadata ESD", null, null, tempExtractionPath, compressionType: WimCompressionType.None, PreserveACL: false, progressCallback: callback);

            Directory.Delete(tmp, true);

            if (!result)
                return null;

            return esdFilePath;
        }
    }
}