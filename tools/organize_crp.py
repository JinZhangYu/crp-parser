#!/usr/bin/env python3

import os
import json
import re
import shutil
import argparse
import sys
from collections import defaultdict
from tqdm import tqdm

def organize_crp_assets(input_dir, output_dir=None, verbose=True):
    """
    Organize extracted CRP assets into instance groups based on GameObject relationships.
    Group related meshes, textures, and materials together with their GameObjects.
    
    Args:
        input_dir (str): Directory containing the extracted CRP files
        output_dir (str): Directory to save organized files (defaults to input_dir/organized)
    
    Returns:
        str: Path to the organized output directory
    """
    input_dir = os.path.abspath(input_dir)
    
    if output_dir is None:
        output_dir = os.path.join(input_dir, "organized")
    
    os.makedirs(output_dir, exist_ok=True)
    if verbose:
        print(f"Organizing CRP assets from {input_dir} to {output_dir}")
    
    # Create main output directories
    instances_dir = os.path.join(output_dir, "instances")
    unassigned_dir = os.path.join(output_dir, "unassigned")
    os.makedirs(instances_dir, exist_ok=True)
    os.makedirs(unassigned_dir, exist_ok=True)
    
    # Create subdirectories for unassigned files
    mesh_dir = os.path.join(unassigned_dir, "mesh")
    material_dir = os.path.join(unassigned_dir, "material")
    texture_dir = os.path.join(unassigned_dir, "texture")
    other_dir = os.path.join(unassigned_dir, "other")
    
    for directory in [mesh_dir, material_dir, texture_dir, other_dir]:
        os.makedirs(directory, exist_ok=True)
    
    # Find all files
    all_files = []
    for filename in os.listdir(input_dir):
        all_files.append(os.path.join(input_dir, filename))
    
    # Function to safely read JSON files
    def safe_read_json(file_path):
        try:
            with open(file_path, 'r', encoding='utf-8') as f:
                return json.load(f)
        except UnicodeDecodeError:
            try:
                # Try with a different encoding
                with open(file_path, 'r', encoding='latin-1') as f:
                    return json.load(f)
            except Exception as e:
                print(f"Error reading file {file_path}: {e}")
                return None
        except Exception as e:
            print(f"Error reading file {file_path}: {e}")
            return None

    # Generate map_idx2filename, map_filename2idx
    map_idx2filename = {}
    map_filename2idx = {}
    for filename in all_files:
        if 'entry' not in filename:
            continue
        
        idx = int(filename.split('entry_')[1].split('_')[0])
        map_idx2filename[idx] = filename
        map_filename2idx[filename] = idx

    if verbose:
        print(f'map_idx2filename {map_idx2filename}')
        print(f'map_filename2idx {map_filename2idx}')

    # get the header.json file
    header_file = None
    for filename in all_files:
        if filename.endswith("header.json"):
            header_file = filename
            break
    if header_file is None:
        raise ValueError("header.json not found in the input directory")
    if verbose:
        print(f'Found header json file {header_file} in the CRP file')

    header = safe_read_json(header_file)
    if header is None:
        raise ValueError(f"Could not read header file {header_file}")
    if verbose:
        print(f'Found {len(header["assets"])} assets in the CRP file')

    # Generate map_idx2hash, map_hash2idx
    map_idx2hash = {}
    map_hash2idx = {}
    idx = 0
    for asset in header["assets"]:
        map_idx2hash[idx] = asset["assetChecksum"]
        map_hash2idx[asset["assetChecksum"]] = idx
        idx += 1
    if verbose:
        print(f'map_idx2hash {map_idx2hash}')
        print(f'map_hash2idx {map_hash2idx}')

    # Get all GameObject files
    gameobject_files = []
    for filename in all_files:
        if filename.endswith("GameObject.json"):
            gameobject_files.append(filename)
    if verbose:
        print(f'Found {len(gameobject_files)} GameObject files in the CRP file')

    # Iterate over instances
    for inst_id in range(len(gameobject_files)):
        if verbose:
            print(f'Processing instance {inst_id}/{len(gameobject_files)}')

        # Load the GameObject file
        gameobject = safe_read_json(gameobject_files[inst_id])
        if gameobject is None:
            print(f"Skipping instance {inst_id} due to error reading GameObject file")
            continue
        if verbose:
            print(f'Loaded GameObject file {gameobject_files[inst_id]}')

        # Get mesh obj hash
        mesh_obj_hash = gameobject["1_UnityEngine.MeshFilter"]
        if verbose:
            print(f'Found mesh obj hash {mesh_obj_hash}')

        # Get material hash
        material_obj_hash = gameobject["2_UnityEngine.MeshRenderer"][0]
        if verbose:
            print(f'Found material obj hash {material_obj_hash}')

        # Get mesh obj filename
        mesh_idx = map_hash2idx[mesh_obj_hash]
        mesh_filename = map_idx2filename[mesh_idx]
        if verbose:
            print(f'Found mesh obj filename {mesh_filename}')

        # Get material obj filename
        material_idx = map_hash2idx[material_obj_hash]
        material_filename = map_idx2filename[material_idx]
        if verbose:
            print(f'Found material obj filename {material_filename}')

        # Read material file
        material = safe_read_json(material_filename)
        if material is None:
            print(f"Error reading material file {material_filename}, using empty material")
            material = {"textures": {}}
        if verbose:
            print(f'Loaded material file {material_filename}')

        # Get texture hashes
        texture_hashes = []
        if "textures" in material:
            for k, v in material["textures"].items():  # Use .items() to iterate over dictionary
                if v:  # Only add non-empty texture references
                    texture_hashes.append(v)
                    if verbose:
                        print(f'Found texture hash {v} for {k}')
        
        # Get texture filenames
        texture_filenames = []
        for texture_hash in texture_hashes:
            try:
                texture_idx = map_hash2idx.get(texture_hash)
                if texture_idx is not None:
                    texture_filename = map_idx2filename.get(texture_idx)
                    if texture_filename:
                        texture_filenames.append(texture_filename)
                        if verbose:
                            print(f'Found texture obj filename {texture_filename}')
                else:
                    # Try to find texture by searching for hash in filenames
                    for file_path in all_files:
                        if texture_hash in file_path and ("Texture" in file_path or file_path.endswith(".png") or file_path.endswith(".jpg")):
                            texture_filenames.append(file_path)
                            print(f'Found texture by searching filename: {os.path.basename(file_path)}')
                            break
            except Exception as e:
                print(f'Error finding texture for hash {texture_hash}: {e}')
                # Try to find by extension
                texture_found = False
                for file_path in all_files:
                    if file_path.endswith(".png") or file_path.endswith(".jpg"):
                        # Check if it's within a reasonable range of the material file
                        material_idx = map_filename2idx.get(material_filename, 0)
                        file_idx = map_filename2idx.get(file_path, 0)
                        if abs(material_idx - file_idx) < 10:  # within 10 entries
                            texture_filenames.append(file_path)
                            print(f'Found nearby texture: {os.path.basename(file_path)}')
                            texture_found = True
                            break
        
        # Create instance directory
        instance_dir = os.path.join(instances_dir, f"instance_{inst_id}")
        os.makedirs(instance_dir, exist_ok=True)

        # Copy gameobject file
        gameobject_dst = os.path.join(instance_dir, os.path.basename(gameobject_files[inst_id]))
        if os.path.exists(gameobject_dst):
            if verbose:
                print(f"GameObject file {gameobject_dst} already exists, skipping copy")
        else:
            shutil.copy(gameobject_files[inst_id], gameobject_dst)
            if verbose:
                print(f"Copied GameObject file to {gameobject_dst}")

        # Copy mesh file
        mesh_dst = os.path.join(instance_dir, os.path.basename(mesh_filename))
        if os.path.exists(mesh_dst):
            if verbose:
                print(f"Mesh file {mesh_dst} already exists, skipping copy")
        else:
            shutil.copy(mesh_filename, mesh_dst)
            if verbose:
                print(f"Copied mesh file to {mesh_dst}")
        
        # Copy material file
        material_dst = os.path.join(instance_dir, os.path.basename(material_filename))
        if os.path.exists(material_dst):
            if verbose:
                print(f"Material file {material_dst} already exists, skipping copy")
        else:
            shutil.copy(material_filename, material_dst)
            if verbose:
                print(f"Copied material file to {material_dst}")
        
        # Copy texture files
        for texture_filename in texture_filenames:
            texture_dst = os.path.join(instance_dir, os.path.basename(texture_filename))
            if os.path.exists(texture_dst):
                if verbose:
                    print(f"Texture file {texture_dst} already exists, skipping copy")
            else:
                shutil.copy(texture_filename, texture_dst)
                if verbose:
                    print(f"Copied texture file to {texture_dst}")
        
        if verbose:
            print("")
    
    # Keep track of all assigned files
    assigned_files = set()
    # assigned_files.add(header_file) # Add header.json
    
    # Go through each instance directory and collect all files that have been assigned
    for inst_id in range(len(gameobject_files)):
        instance_dir = os.path.join(instances_dir, f"instance_{inst_id}")
        if os.path.exists(instance_dir):
            for filename in os.listdir(instance_dir):
                original_path = os.path.join(input_dir, os.path.basename(filename))
                assigned_files.add(original_path)
                # Also add the actual file used
                full_path = os.path.join(input_dir, filename)
                if os.path.exists(full_path):
                    assigned_files.add(full_path)
    
    # Process unassigned files
    if verbose:
        print("\nProcessing unassigned files...")
    for file_path in all_files:
        if file_path not in assigned_files:
            filename = os.path.basename(file_path)
            
            # Determine file type
            if filename.endswith('.obj') or "Mesh" in filename:
                dest_dir = mesh_dir
                file_type = "mesh"
            elif filename.endswith('.json') and "Material" in filename:
                dest_dir = material_dir
                file_type = "material"
            elif filename.endswith('.png') or filename.endswith('.jpg') or "Texture" in filename:
                dest_dir = texture_dir
                file_type = "texture"
            else:
                dest_dir = other_dir
                file_type = "other"
            
            # Copy file to appropriate unassigned directory
            dest_path = os.path.join(dest_dir, filename)
            shutil.copy2(file_path, dest_path)
            if verbose:
                print(f"  Placed unassigned {file_type}: {filename}")
    
    print(f"Organization complete. Results saved to {output_dir}")
    return output_dir

def process_crp_folders(input_file, verbose=True):
    """Process a list of CRP folders from a file.
    
    Args:
        input_file (str): Path to a file containing a list of extracted CRP folders (one per line)
    
    Returns:
        int: 0 if all folders processed successfully, 1 otherwise
    """
    print(f"Reading CRP folder list from {input_file}")
    
    try:
        with open(input_file, 'r') as f:
            crp_folders = [line.strip() for line in f if line.strip()]
    except Exception as e:
        print(f"Error reading input file {input_file}: {e}", file=sys.stderr)
        return 1
    
    print(f"Found {len(crp_folders)} CRP folders to process")
    
    success_count = 0
    failure_count = 0
    
    for folder in tqdm(crp_folders):
        print(f"\nProcessing folder: {folder}")
        folder_path = os.path.abspath(folder)
        
        if not os.path.isdir(folder_path):
            print(f"  Error: {folder_path} is not a directory", file=sys.stderr)
            failure_count += 1
            continue
        
        # Generate output directory name by appending _organized
        output_path = f"{folder_path}_organized"
        
        try:
            organize_crp_assets(folder_path, output_path, verbose)
            success_count += 1
            print(f"  Success: Output saved to {output_path}")
        except Exception as e:
            print(f"  Error processing {folder_path}: {e}", file=sys.stderr)
            import traceback
            traceback.print_exc()
            failure_count += 1
    
    # Print summary
    print(f"\nProcessing complete: {success_count} succeeded, {failure_count} failed")
    return 0 if failure_count == 0 else 1

def main():
    parser = argparse.ArgumentParser(description='Organize extracted CRP assets into instances')
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument('-i', '--input', help='Directory containing extracted CRP files')
    group.add_argument('-f', '--file', help='File containing a list of extracted CRP folders (one per line)')
    parser.add_argument('-o', '--output', help='Output directory for organized files (default: input_dir/organized)')
    parser.add_argument('-v', '--verbose', action='store_true', help='Show detailed processing information')
    
    args = parser.parse_args()
    
    # Determine verbosity - by default, be quiet unless explicitly requested
    verbose = args.verbose
    
    if args.file:
        return process_crp_folders(args.file, verbose)
    else:
        organize_crp_assets(args.input, args.output, verbose)
        return 0


if __name__ == "__main__":
    try:
        main()
    except Exception as e:
        import traceback
        print(f"\nERROR: {str(e)}")
        traceback.print_exc()
