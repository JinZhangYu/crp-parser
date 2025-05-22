using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApplication1.Parsers
{
    class MeshParser
    {
        public static Mesh parseMesh(CrpReader reader, bool saveFile, string saveFileName, long fileSize, bool verbose)
        {
            try
            {
                long fileContentBegin = reader.BaseStream.Position;
                Mesh retVal = new Mesh();
                
                // Create safer versions of reading Unity arrays with better error handling
                try { retVal.vertices = reader.readUnityArray("UnityEngine.Vector3"); } 
                catch (Exception ex) { 
                    if (verbose) Console.WriteLine($"Error reading vertices: {ex.Message}");
                    retVal.vertices = new Vector3[0]; 
                }
                
                try { retVal.colors = reader.readUnityArray("UnityEngine.Color"); } 
                catch (Exception ex) { 
                    if (verbose) Console.WriteLine($"Error reading colors: {ex.Message}");
                    retVal.colors = new Color[0]; 
                }
                
                try { retVal.uv = reader.readUnityArray("UnityEngine.Vector2"); } 
                catch (Exception ex) { 
                    if (verbose) Console.WriteLine($"Error reading UVs: {ex.Message}");
                    retVal.uv = new Vector2[0]; 
                }
                
                try { retVal.normals = reader.readUnityArray("UnityEngine.Vector3"); } 
                catch (Exception ex) { 
                    if (verbose) Console.WriteLine($"Error reading normals: {ex.Message}");
                    retVal.normals = new Vector3[0]; 
                }
                
                try { retVal.tangents = reader.readUnityArray("UnityEngine.Vector4"); } 
                catch (Exception ex) { 
                    if (verbose) Console.WriteLine($"Error reading tangents: {ex.Message}");
                    retVal.tangents = new Vector4[0]; 
                }
                
                try { retVal.boneWeights = reader.readUnityArray("UnityEngine.BoneWeight"); } 
                catch (Exception ex) { 
                    if (verbose) Console.WriteLine($"Error reading bone weights: {ex.Message}");
                    retVal.boneWeights = new Boneweight[0]; 
                }
                
                try { retVal.bindPoses = reader.readUnityArray("UnityEngine.Matrix4x4"); } 
                catch (Exception ex) { 
                    if (verbose) Console.WriteLine($"Error reading bind poses: {ex.Message}");
                    retVal.bindPoses = new Matrix4x4[0]; 
                }
                
                // Read submesh data with error handling
                try 
                { 
                    retVal.subMeshCount = reader.ReadInt32();
                    
                    // Validate submesh count is reasonable (often corrupted in files)
                    if (retVal.subMeshCount < 0 || retVal.subMeshCount > 1000) // Arbitrary upper limit
                    {
                        if (verbose) Console.WriteLine($"Invalid submesh count: {retVal.subMeshCount}, using 0");
                        retVal.subMeshCount = 0;
                    }
                    
                    for (int i = 0; i < retVal.subMeshCount; i++)
                    {
                        try
                        {
                            int[] triangles = reader.readUnityArray("System.Int32");
                            retVal.triangles.AddRange(triangles);
                        }
                        catch (Exception ex)
                        {
                            if (verbose) Console.WriteLine($"Error reading triangles for submesh {i}: {ex.Message}");
                        }
                    }
                } 
                catch (Exception ex) 
                { 
                    if (verbose) Console.WriteLine($"Error reading submesh count: {ex.Message}");
                    retVal.subMeshCount = 0; 
                }
                
                // If we haven't read all the data yet, just skip the rest
                long currentPos = reader.BaseStream.Position;
                if (currentPos < fileContentBegin + fileSize)
                {
                    try
                    {
                        int bytesToRead = (int)(fileSize - (currentPos - fileContentBegin));
                        if (bytesToRead > 0 && bytesToRead < 100000000) // Sanity check
                        {
                            reader.ReadBytes(bytesToRead);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (verbose) Console.WriteLine($"Error skipping remaining bytes: {ex.Message}");
                    }
                }
                
                string fileName = saveFileName + ".obj";
                if (verbose)
                {
                    Console.WriteLine("Read mesh data with {0} vertices, {1} triangles", 
                        retVal.vertices?.Length ?? 0, retVal.triangles?.Count ?? 0);
                }
                
                // Only save if we have some valid mesh data
                if (saveFile && retVal.vertices != null && retVal.vertices.Length > 0)
                {
                    try
                    {
                        StreamWriter file = new StreamWriter(new FileStream(fileName, FileMode.Create));
                        file.Write(retVal.exportObj());
                        file.Close();
                        
                        if (verbose)
                        {
                            Console.WriteLine("Saved mesh to {0}", fileName);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (verbose) Console.WriteLine($"Error saving OBJ file: {ex.Message}");
                    }
                }
                else if (verbose)
                {
                    Console.WriteLine("Mesh has no vertices, not saving OBJ file");
                }
                
                return retVal;
            }
            catch (Exception ex)
            {
                if (verbose) Console.WriteLine($"Critical error parsing mesh: {ex.Message}");
                return new Mesh(); // Return empty mesh on critical failure
            }
        }

    }
}
