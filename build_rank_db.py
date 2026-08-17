#!/usr/bin/env python3
"""Scan F:\MangaDL\wnacg\wnacg.com\同人誌 folders and build rank_display_db.json"""
import os
import json
import glob

BASE_DIR = r"F:\MangaDL\wnacg\wnacg.com\同人誌"
OUTPUT_FILE = r"C:\source\repos\MangaViewer\data\rank_display_db.json"

folders_data = {}

for entry in os.listdir(BASE_DIR):
    folder_path = os.path.join(BASE_DIR, entry)
    if not os.path.isdir(folder_path):
        continue
    
    json_file = os.path.join(folder_path, f"{entry}.json")
    if not os.path.exists(json_file):
        continue
    
    try:
        with open(json_file, "r", encoding="utf-8") as f:
            data = json.load(f)
    except Exception:
        continue

    # ratingキーが存在しない、または無効な値の場合はスキップ
    if "rating" not in data or not isinstance(data["rating"], (int, float)):
        continue
    
    rating = data["rating"]
    image_count = data.get("imageCount", 0)
    
    # Count CBZ files
    cbz_files = glob.glob(os.path.join(folder_path, "*.cbz"))
    cbz_count = len(cbz_files)
    
    # Estimate max volume from folder name or CBZ filenames
    max_volume = 0
    for cbz in cbz_files:
        basename = os.path.basename(cbz).lower()
        # Look for volume patterns like "v01", "vol02", "#03"
        import re
        m = re.search(r'(?:v(?:ol)?|vol|#)\s*(\d+)', basename)
        if m:
            vol = int(m.group(1))
            if vol > max_volume:
                max_volume = vol
    if max_volume == 0 and cbz_count > 0:
        max_volume = cbz_count
    
    # Extract keywords from folder name (simple approach)
    keywords = entry.replace("[", ",").replace("]", ",").replace("(", ",").replace(")", ",")
    keywords = ",".join([k.strip() for k in keywords.split(",") if k.strip()])
    
    folders_data[folder_path] = {
        "rating": rating,
        "imageCount": image_count,
        "folderName": entry,
        "keywords": keywords,
        "cbzCount": cbz_count,
        "maxVolume": max_volume,
        "isRead": False
    }

output = {"folders": folders_data}
with open(OUTPUT_FILE, "w", encoding="utf-8") as f:
    json.dump(output, f, indent=2, ensure_ascii=False)

print(f"Generated {OUTPUT_FILE} with {len(folders_data)} folder entries.")