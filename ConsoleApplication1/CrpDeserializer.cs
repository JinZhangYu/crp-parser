using ConsoleApplication1.Parsers;
using CrpParser;
using CrpParser.Utils;
using ImageMagick;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace ConsoleApplication1
{
	public class CrpDeserializer
    {
        public string filePath;
        public string outputFolder;
        private FileStream stream;
        private CrpReader reader;
        private AssetParser assetParser;
        private Dictionary<string, int> typeRefCount = new Dictionary<string, int>();
        private string crpHash;

		/// <summary>
		/// Initializes the object by opening the specified CRP file. Note and be prepared to handle the exceptions
		/// that may be thrown.
		/// </summary>
		/// <param name="filePath">Path to the CRP file that needs to be opened.</param>
		/// <exception cref="System.IO.IOException">Thrown if i.e the filePath parameter is incorrect, file is missing,
		/// in use, etc.</exception>
		public CrpDeserializer(string filePath, string outputFolder)
        {
            this.filePath = filePath;
            this.outputFolder = string.IsNullOrEmpty(outputFolder) 
                                ? Path.GetDirectoryName(filePath)
                                : outputFolder;
            // Extract the CRP hash from the filename
            this.crpHash = Path.GetFileNameWithoutExtension(filePath);
			stream = File.Open(filePath, FileMode.Open);
            reader = new CrpReader(stream);
            assetParser = new AssetParser(reader);
        }

        public void parseFile(Options options)
        {
            string magicStr = new string(reader.ReadChars(4));
            if (magicStr.Equals(Consts.MAGICSTR))
            {
                CrpHeader header = parseHeader();

                if (options.SaveFiles)
                {
                    // Use CRP hash as folder name
                    string path = Path.Combine(outputFolder, crpHash);
                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                    }
                    Environment.CurrentDirectory = (path);
                }

                if (options.Verbose)
                {
                    Console.WriteLine(header);
                }
                if (options.SaveFiles)
                {
                    StreamWriter file = new StreamWriter(new FileStream(crpHash + "_header.json", FileMode.Create));
                    string json = JsonConvert.SerializeObject(header, Formatting.Indented, new Newtonsoft.Json.Converters.StringEnumConverter());
                    file.Write(json);
                    file.Close();
                }
                if (header.isLut)
                {
                    parseLut(header, options.SaveFiles, options.Verbose);
                }
                else
                {
                    // First, get the absolute position where asset content begins
                    long absoluteContentBegin = header.contentBeginIndex;
                
                    for (int i = 0; i < header.numAssets; i++)
                    {
                        try {
                            // Reset stream position to the beginning of this specific asset
                            stream.Position = absoluteContentBegin + header.assets[i].assetOffsetBegin;
                        
                            // Re-initialize reader with current stream position
                            reader = new CrpReader(stream);
                        
                            // Parse the asset
                            if (options.Verbose)
                            {
                                Console.WriteLine($"Processing asset {i+1}/{header.numAssets}: {header.assets[i].assetName}");
                                Console.WriteLine($"Asset position: {stream.Position}, Asset size: {header.assets[i].assetSize}");
                            }
                        
                            parseAssets(header, i, options.SaveFiles, options.Verbose);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error during asset {i+1} processing: {ex.Message}");
                            if (options.Verbose)
                            {
                                Console.WriteLine(ex.StackTrace);
                            }
                        }
                    }   
                }
            }
            else
            {
                throw new InvalidDataException("Invalid file format!");
            }
        }

        private CrpHeader parseHeader()
        {
            CrpHeader output = new CrpHeader();
            output.formatVersion = reader.ReadUInt16();
            output.packageName = reader.ReadString();
            string encryptedAuthor = reader.ReadString();
            if (encryptedAuthor.Length > 0)
            {
                output.authorName = CryptoUtils.Decrypt(encryptedAuthor);
            }
            else
            {
                output.authorName = "Unknown";
            }
            output.pkgVersion = reader.ReadUInt32();
            output.mainAssetName = reader.ReadString();
            output.numAssets = reader.ReadInt32();
            output.contentBeginIndex = reader.ReadInt64();

            output.assets = new List<CrpAssetInfoHeader>();
            for (int i = 0; i < output.numAssets; i++)
            {
                CrpAssetInfoHeader info = new CrpAssetInfoHeader();
                info.assetName = reader.ReadString();
                info.assetChecksum = reader.ReadString();
                info.assetType = (Consts.AssetTypeMapping)(reader.ReadInt32());
                if(info.assetType == Consts.AssetTypeMapping.userLut)
                {
                    output.isLut = true;
                }
                info.assetOffsetBegin = reader.ReadInt64();
                info.assetSize = reader.ReadInt64();
                output.assets.Add(info);

            }

            return output;
        }

        /// <summary>
        /// Special Parser for LUTs, we're only grabbing the headerless PNG file (for now)
        /// </summary>
        /// <param name="header"></param>
        /// <param name="saveFiles"></param>
        /// <param name="isVerbose"></param>
        private void parseLut(CrpHeader header, bool saveFiles, bool isVerbose)
        {
            //Find the first instance of data(PNG file)
            CrpAssetInfoHeader info = header.assets.Find(asset => asset.assetName.Contains(Consts.DATA_EXTENSION));

            //Generate a name for the file using the new format
            string fileName = string.Format("{0}_entry_lut_{1}", crpHash, "UnityEngine.Texture2D");

            //Should be unnessecary in current version(stream pointer should already be at start of file),
            //but advance stream pointer to file position
            reader.BaseStream.Seek(info.assetOffsetBegin, SeekOrigin.Current);

            //Read file and deal with it as apporiate.
            MagickImage retVal = ImgParser.parseImgFile(reader, (uint)info.assetSize);
            if (isVerbose)
            {
                Console.WriteLine("Read image file {0}", fileName);
            }
            if (saveFiles)
            {
                retVal.Write(fileName + ".png");
            }
        }

        private void parseAssets(CrpHeader header, int index, bool saveFiles, bool isVerbose)
        {
            long contentBeginPos = header.contentBeginIndex;
            try
            {
                try
                {
                    // Try the standard method for all asset types
                    bool isNullFlag = reader.ReadBoolean();
                    if (!isNullFlag)
                    {
                        string assemblyQualifiedName = reader.ReadString();
                        string specificType = assemblyQualifiedName.Split(new char[] { ',' })[0];
                        long assetContentLen = header.assets[index].assetSize - (2 + assemblyQualifiedName.Length);
                        string assetName = reader.ReadString();
                        assetContentLen -= (1 + assetName.Length);

                        // Create filename based on the correct pattern
                        string fileName = string.Format("{0}_entry_{1}_{2}", crpHash, index, specificType);
                        
                        if (isVerbose)
                        {
                            Console.WriteLine($"Asset {index}: {specificType}, Length: {assetContentLen}, Name: {assetName}");
                        }
                        
                        // Use the asset parser with the correct type for all assets
                        assetParser.parseObject((int)assetContentLen, specificType, saveFiles, fileName, isVerbose);
                    }
                    else if (isVerbose)
                    {
                        Console.WriteLine($"Asset {index} is null");
                        
                        // Save raw data for null assets
                        if (saveFiles)
                        {
                            try 
                            {
                                string rawFilename = string.Format("{0}_entry_{1}_raw", crpHash, index);
                                byte[] rawData = reader.ReadBytes((int)header.assets[index].assetSize - 1); // -1 for the bool we already read
                                File.WriteAllBytes(rawFilename + ".bin", rawData);
                                if (isVerbose)
                                {
                                    Console.WriteLine($"Saved raw data for null asset to {rawFilename}.bin");
                                }
                            }
                            catch (Exception rawEx)
                            {
                                Console.WriteLine($"Error saving raw data for null asset: {rawEx.Message}");
                            }
                        }
                    }
                }
                catch (Exception parseEx)
                {
                    Console.WriteLine($"Error in standard parsing for asset {index}: {parseEx.Message}");
                    
                    // Fallback to raw data extraction
                    if (saveFiles)
                    {
                        try 
                        {
                            // Reset position to the start of this asset
                            stream.Position = contentBeginPos + header.assets[index].assetOffsetBegin;
                            reader = new CrpReader(stream);
                            
                            string rawFilename = string.Format("{0}_entry_{1}_raw", crpHash, index);
                            byte[] rawData = reader.ReadBytes((int)header.assets[index].assetSize);
                            File.WriteAllBytes(rawFilename + ".bin", rawData);
                            if (isVerbose)
                            {
                                Console.WriteLine($"Saved raw asset data to {rawFilename}.bin");
                            }
                        }
                        catch (Exception rawEx)
                        {
                            Console.WriteLine($"Error saving raw asset data: {rawEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing asset {index}: {ex.Message}");
                if (isVerbose)
                {
                    Console.WriteLine(ex.StackTrace);
                }
            }
        }
        
        /// <summary>
        /// Cleans a filename by removing invalid characters and truncating if too long.
        /// </summary>
        private string CleanFilename(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return "unnamed";
                
            // Replace invalid characters with underscores
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string cleanName = string.Join("_", filename.Split(invalidChars));
            
            // Truncate if too long (Windows has a 260 character path limit, but we'll be more conservative)
            const int MaxFileNameLength = 100;
            if (cleanName.Length > MaxFileNameLength)
            {
                cleanName = cleanName.Substring(0, MaxFileNameLength - 10) + "_" + 
                           Guid.NewGuid().ToString().Substring(0, 8); // Add a unique identifier
            }
            
            return cleanName;
        }

    }
}
