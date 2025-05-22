#!/usr/bin/env python3

import argparse
import os
import subprocess
import sys
from tqdm import tqdm

def process_crp_files(input_file, dotnet_path, crp_parser_dir):
    """
    Process a list of CRP files using CrpParser.dll
    
    Args:
        input_file (str): Path to a file containing a list of CRP files (one per line)
        dotnet_path (str): Path to the dotnet executable
        crp_parser_dir (str): Path to the directory containing CrpParser.dll
    """
    # Validate inputs
    if not os.path.exists(input_file):
        print(f"Error: Input file '{input_file}' does not exist.", file=sys.stderr)
        return 1
    
    if not os.path.exists(dotnet_path):
        print(f"Error: Dotnet path '{dotnet_path}' does not exist.", file=sys.stderr)
        return 1
    
    if not os.path.exists(crp_parser_dir):
        print(f"Error: CRP parser directory '{crp_parser_dir}' does not exist.", file=sys.stderr)
        return 1
    
    crp_parser_dll = os.path.join(crp_parser_dir, "CrpParser.dll")
    if not os.path.exists(crp_parser_dll):
        print(f"Error: CrpParser.dll not found in '{crp_parser_dir}'.", file=sys.stderr)
        return 1
    
    # Read the list of CRP files
    try:
        with open(input_file, 'r') as f:
            crp_files = [line.strip() for line in f if line.strip()]
    except Exception as e:
        print(f"Error reading input file: {e}", file=sys.stderr)
        return 1
    
    if not crp_files:
        print("No CRP files found in the input file.", file=sys.stderr)
        return 1
    
    # Process each CRP file
    success_count = 0
    failure_count = 0
    
    for i, crp_file in enumerate(tqdm(crp_files)):
        if not os.path.exists(crp_file):
            print(f"Warning: CRP file '{crp_file}' does not exist. Skipping.", file=sys.stderr)
            failure_count += 1
            continue
        
        # Generate output log filename
        log_file = crp_file.replace(".crp", ".log")
        
        # Construct the command
        cmd = [
            dotnet_path,
            crp_parser_dll,
            "-f", crp_file,
            "-s",  # Silent mode
            "-v"   # Verbose
        ]
        
        print(f"Processing [{i+1}/{len(crp_files)}]: {crp_file}")
        
        try:
            # Run the command and redirect output to the log file
            with open(log_file, 'w') as log:
                result = subprocess.run(
                    cmd,
                    cwd=crp_parser_dir,
                    stdout=log,
                    stderr=subprocess.STDOUT,
                    text=True
                )
            
            if result.returncode == 0:
                print(f"  Success: Output saved to {log_file}")
                success_count += 1
            else:
                print(f"  Failed: See {log_file} for details", file=sys.stderr)
                failure_count += 1
                
        except Exception as e:
            print(f"  Error processing {crp_file}: {e}", file=sys.stderr)
            failure_count += 1
    
    # Print summary
    print(f"\nProcessing complete: {success_count} succeeded, {failure_count} failed")
    return 0 if failure_count == 0 else 1

def main():
    parser = argparse.ArgumentParser(description='Process a list of CRP files using CrpParser.dll')
    parser.add_argument('-i', '--input', required=True, help='File containing a list of CRP files (one per line)')
    parser.add_argument('-d', '--dotnet', required=True, help='Path to the dotnet executable')
    parser.add_argument('-p', '--parser-dir', required=True, help='Path to the directory containing CrpParser.dll')
    
    args = parser.parse_args()
    
    return process_crp_files(args.input, args.dotnet, args.parser_dir)

if __name__ == "__main__":
    sys.exit(main())
