using ImageMagick;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApplication1.Parsers
{
    public static class ImgParser
    {
        private static readonly byte[] PNG_HEADER = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        private static readonly byte[] DDS_HEADER = new byte[] { 68, 68, 83, 32 };

        public static MagickImage parseImage(CrpReader reader, bool saveFile, string saveFileName, long fileSize, bool verbose)
        {
            bool forceLinearFlag = reader.ReadBoolean();
            uint imgLength = reader.ReadUInt32();
            
            if (verbose)
            {
                Console.WriteLine("parseImage, saveFileName: {0}, fileSize: {1}, imgLength {2}", saveFileName, fileSize, imgLength);
            }

            // Use fileSize instead of imgLength
            uint actualImageSize = (uint)(fileSize - 5);  // 5 = 1 byte (boolean) + 4 bytes (uint)
            MagickImage retVal = parseImgFile(reader, actualImageSize);

            string fileName = saveFileName + ".png";
            
            if (verbose)
            {
                Console.WriteLine("Read image file {0}", fileName);
            }
            if (saveFile)
            {
                // Ensure directory exists
                string directory = Path.GetDirectoryName(fileName);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                retVal.Write(fileName);
            }
            
            return retVal;
        }

        public static MagickImage parseImgFile(CrpReader reader, uint fileSize)
        {
            if (fileSize == 0)
            {
                throw new InvalidDataException("File size cannot be zero");
            }

            // Read the image data using the provided fileSize
            byte[] imageData = reader.ReadBytes((int)fileSize);
            
            if (imageData == null || imageData.Length == 0)
            {
                throw new InvalidDataException($"Failed to read image data: requested {fileSize} bytes");
            }

            // Check for PNG header
            bool isPng = CheckForSequence(imageData, PNG_HEADER);
            
            // Check for DDS header
            bool isDds = CheckForSequence(imageData, DDS_HEADER);
            
            if (isDds)
            {
                // Handle DDS format
                int ddsOffset = FindSequenceOffset(imageData, DDS_HEADER);
                if (ddsOffset >= 0)
                {
                    byte[] ddsData = new byte[imageData.Length - ddsOffset];
                    Array.Copy(imageData, ddsOffset, ddsData, 0, ddsData.Length);
                    var image = new MagickImage(ddsData);
                    image.Format = MagickFormat.Dds;
                    return image;
                }
            }
            else if (isPng)
            {
                // Handle PNG format
                int pngOffset = FindSequenceOffset(imageData, PNG_HEADER);
                if (pngOffset >= 0)
                {
                    byte[] pngData = new byte[imageData.Length - pngOffset];
                    Array.Copy(imageData, pngOffset, pngData, 0, pngData.Length);
                    var image = new MagickImage(pngData);
                    image.Format = MagickFormat.Png;
                    return image;
                }
            }
            
            // If no specific format is detected or no header found, return as raw image
            var defaultImage = new MagickImage(imageData);
            defaultImage.Format = MagickFormat.Png; // Default to PNG
            return defaultImage;
        }

        private static bool CheckForSequence(byte[] data, byte[] sequence)
        {
            return FindSequenceOffset(data, sequence) >= 0;
        }

        private static int FindSequenceOffset(byte[] data, byte[] sequence)
        {
            for (int i = 0; i <= data.Length - sequence.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < sequence.Length; j++)
                {
                    if (data[i + j] != sequence[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}

