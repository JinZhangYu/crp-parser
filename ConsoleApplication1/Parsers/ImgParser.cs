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
            try
            {
                bool forceLinearFlag = reader.ReadBoolean();
                uint imgLength = reader.ReadUInt32();
                
                if (verbose)
                {
                    Console.WriteLine("parseImage, saveFileName: {0}, fileSize: {1}, imgLength {2}", saveFileName, fileSize, imgLength);
                }

                // Calculate actual image size based on the file size minus header size
                // 5 = 1 byte (boolean) + 4 bytes (uint)
                uint actualImageSize = (uint)(fileSize - 5);
                
                if (verbose)
                {
                    Console.WriteLine("Actual image size: {0} bytes", actualImageSize);
                }
                
                MagickImage retVal = parseImgFile(reader, actualImageSize);
                string fileName = saveFileName + ".png";
                
                if (verbose)
                {
                    Console.WriteLine("Read image file {0}, dimensions: {1}x{2}", fileName, retVal.Width, retVal.Height);
                }
                
                if (saveFile)
                {
                    // Ensure directory exists
                    string directory = Path.GetDirectoryName(fileName);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    
                    // Apply auto-orientation and strip metadata for cleaner output
                    retVal.AutoOrient();
                    retVal.Strip();
                    
                    // Save image
                    retVal.Write(fileName);
                    
                    if (verbose)
                    {
                        Console.WriteLine("Saved image to {0}", fileName);
                    }
                }
                
                return retVal;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error parsing image: {0}", ex.Message);
                if (verbose)
                {
                    Console.WriteLine(ex.StackTrace);
                }
                
                // Return an empty image on error
                return new MagickImage();
            }
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

            // Special handling for Cities Skylines textures
            // Look for known texture headers at different offsets
            // Sometimes textures in Cities Skylines have padding or metadata before the actual image data
            
            // Check for common image format headers throughout the data
            for (int offset = 0; offset < imageData.Length - 16; offset++)
            {
                // Check for DDS header
                if (offset + 4 < imageData.Length && 
                    imageData[offset] == 'D' && imageData[offset+1] == 'D' && 
                    imageData[offset+2] == 'S' && imageData[offset+3] == ' ')
                {
                    try
                    {
                        byte[] ddsData = new byte[imageData.Length - offset];
                        Array.Copy(imageData, offset, ddsData, 0, ddsData.Length);
                        
                        var image = new MagickImage();
                        image.Read(ddsData, MagickFormat.Dds);
                        image.Format = MagickFormat.Png; // Convert to PNG for better compatibility
                        return image;
                    }
                    catch (Exception)
                    {
                        // Continue searching if this offset didn't work
                    }
                }
                
                // Check for PNG header
                if (offset + 8 < imageData.Length &&
                    imageData[offset] == 137 && imageData[offset+1] == 80 && 
                    imageData[offset+2] == 78 && imageData[offset+3] == 71 &&
                    imageData[offset+4] == 13 && imageData[offset+5] == 10 &&
                    imageData[offset+6] == 26 && imageData[offset+7] == 10)
                {
                    try
                    {
                        byte[] pngData = new byte[imageData.Length - offset];
                        Array.Copy(imageData, offset, pngData, 0, pngData.Length);
                        
                        var image = new MagickImage();
                        image.Read(pngData, MagickFormat.Png);
                        return image;
                    }
                    catch (Exception)
                    {
                        // Continue searching if this offset didn't work
                    }
                }
                
                // Check for JPEG header (multiple possible signatures)
                if ((offset + 4 < imageData.Length &&
                     imageData[offset] == 0xFF && imageData[offset+1] == 0xD8 && 
                     imageData[offset+2] == 0xFF && (imageData[offset+3] == 0xE0 || 
                                                     imageData[offset+3] == 0xE1 || 
                                                     imageData[offset+3] == 0xDB)))
                {
                    try
                    {
                        byte[] jpegData = new byte[imageData.Length - offset];
                        Array.Copy(imageData, offset, jpegData, 0, jpegData.Length);
                        
                        var image = new MagickImage();
                        image.Read(jpegData, MagickFormat.Jpeg);
                        return image;
                    }
                    catch (Exception)
                    {
                        // Continue searching if this offset didn't work
                    }
                }
            }
            
            // If we couldn't find any known image format, try using raw pixel data
            // This is common in Cities Skylines where textures might be stored in a custom format
            try
            {
                // Try to use ImageMagick's RGBA format assuming common texture dimensions
                int possibleSqrtSize = (int)Math.Sqrt(imageData.Length / 4); // Assuming 4 bytes per pixel (RGBA)
                
                if (possibleSqrtSize > 0 && possibleSqrtSize * possibleSqrtSize * 4 == imageData.Length)
                {
                    // It's likely a square RGBA texture
                    var image = new MagickImage(imageData, new MagickReadSettings 
                    { 
                        Format = MagickFormat.Rgba, 
                        Width = possibleSqrtSize, 
                        Height = possibleSqrtSize 
                    });
                    return image;
                }
                else
                {
                    // Try common texture dimensions: 1024x1024, 512x512, 256x256, 128x128, 64x64
                    int[] commonSizes = { 1024, 512, 256, 128, 64 };
                    
                    foreach (int size in commonSizes)
                    {
                        if (imageData.Length >= size * size * 3) // At least RGB data
                        {
                            try
                            {
                                var image = new MagickImage(imageData, new MagickReadSettings 
                                { 
                                    Format = MagickFormat.Rgb, 
                                    Width = size, 
                                    Height = size 
                                });
                                return image;
                            }
                            catch
                            {
                                // Try next size
                            }
                        }
                    }
                }
                
                // Last attempt: try to read it directly
                var defaultImage = new MagickImage();
                defaultImage.Read(imageData);
                defaultImage.Format = MagickFormat.Png;
                return defaultImage;
            }
            catch (Exception)
            {
                // Last resort: create a new empty image
                var emptyImage = new MagickImage(MagickColors.Transparent, 1, 1);
                emptyImage.Format = MagickFormat.Png;
                return emptyImage;
            }
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

