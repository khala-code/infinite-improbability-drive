import os, glob
files = sorted(glob.glob('./frames/hpl_radial_t*.png'))
print(f'files found: {len(files)}')
for i, f in enumerate(files):
    print(f)
    os.rename(f, f'./frames/frame_{i:03d}.png')