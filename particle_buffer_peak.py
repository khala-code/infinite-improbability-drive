import numpy as np
data = np.frombuffer(open(r"Assets\StreamingAssets\CMB\particle_buffer.bin","rb").read(), dtype=np.float32).reshape(-1,24)

# Print fields 10-17 for first 5 particles
print("fields 10-17 for first 5 particles:")
print(data[:5, 10:18])

# Check which offset has values that look like class IDs (0.0, 1.0, 2.0)
# for i in range(10, 20):
#     uvals = np.unique(np.round(data[:, i]).astype(int))
#     print(f"  field[{i}]: unique rounded values = {uvals[:8]}")

print("field[10] range:", data[:,10].min(), data[:,10].max())
print("field[11] range:", data[:,11].min(), data[:,11].max())
print("field[12] range:", data[:,12].min(), data[:,12].max())
print("field[13] range:", data[:,13].min(), data[:,13].max())
print("field[18] range:", data[:,18].min(), data[:,18].max())
print("field[19] unique:", np.unique(data[:,19].round().astype(int)))
print()
print("class counts at field 19:")
pc = data[:,19].round().astype(int)
for c,n in zip(*np.unique(pc, return_counts=True)):
    print(f"  class {c}: {n:,}")
