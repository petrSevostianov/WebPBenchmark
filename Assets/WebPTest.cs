using UnityEngine;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;





#if UNITY_EDITOR
using UnityEditor;

#endif

#nullable enable


public class WebPTest : MonoBehaviour {
	const int NumWorkerThreads = 8;
	const int InputQueueMaxSize = 16;
	BlockingCollection<byte[]> _inputQueue = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>(), InputQueueMaxSize);
	Thread _managerThread;
	Thread[] _workerThreads;

	class WorkerSlot<J, R> where J : class where R : class {
		private J? _job; // input null means end of jobs
		private R? _result;   // output
		public ManualResetEventSlim JobReady = new();   // signaled by manager
		public ManualResetEventSlim ResultReady = new(); // signaled by worker
		public void SetJob(J? job) {
			_job = job;
			JobReady.Set();
		}
		public J? GetJob() {
			JobReady.Wait();
			JobReady.Reset();
			return _job;
		}
		public void SetResult(R? result) {
			_result = result;
			ResultReady.Set();
		}
		public R? GetResult() {
			ResultReady.Wait();
			ResultReady.Reset();
			return _result!;
		}
	}

	WorkerSlot<string, byte[]>[] _workerSlots;

	public string[] FilePaths;
	public InspectorButton _SelectWebPFile;
	public void SelectWebPFile() {
#if UNITY_EDITOR
		var directory = EditorUtility.OpenFolderPanel("Select WebP File", "", "");
		if (string.IsNullOrEmpty(directory)) return;
		var sequenceFiles = System.IO.Directory.GetFiles(directory!, "*.webp");
		//sort by name
		System.Array.Sort(sequenceFiles);

		FilePaths = sequenceFiles;
#endif
	}

	public int Width;
	public int Height;
	//start time and current frame count to calculate fps
	public float StartTime = 0;
	public int FrameCount;
	public float Fps;

	void Start() {
		_workerSlots = new WorkerSlot<string, byte[]>[NumWorkerThreads];
		for (int i = 0; i < NumWorkerThreads; i++) {
			_workerSlots[i] = new();
		}
		_workerThreads = new Thread[NumWorkerThreads];
		for (int i = 0; i < NumWorkerThreads; i++) {
			var slot = _workerSlots[i];
			int index = i;
			_workerThreads[i] = new Thread(() => WorkerThread(index));
			_workerThreads[i].Start();
		}

		_managerThread = new Thread(ManagerThread);
		_managerThread.Start();
	}

	void ManagerThread() {
		for (int i = 0; i < NumWorkerThreads; i++) {
			_workerSlots[i].SetJob(FilePaths[i]);
		}

		for (int i = 0; i < FilePaths.Length; i++) {
			int workerIndex = i % NumWorkerThreads;
			var result = _workerSlots[workerIndex].GetResult();
			_inputQueue.Add(result!);
			//assign new job
			int nextFileIndex = i + NumWorkerThreads;
			if (nextFileIndex >= FilePaths.Length)
				_workerSlots[workerIndex].SetJob(null);
			else
				_workerSlots[workerIndex].SetJob(FilePaths[nextFileIndex]);
		}
		_inputQueue.CompleteAdding();
	}

	ConcurrentQueue<byte[]>? bufferPool;

	byte[]? GetBuffer() {
		if (bufferPool == null) return null;
		return bufferPool.TryDequeue(out var buffer) ? buffer : null;
	}

	void ReturnBuffer(byte[] buffer) {
		if (bufferPool == null)
			bufferPool = new ConcurrentQueue<byte[]>();
		bufferPool.Enqueue(buffer);
	}

	void WorkerThread(int index) {
		var slot = _workerSlots[index];
		while (true) {
			var filePath = slot.GetJob();
			if (filePath == null) break;

			var webpData = System.IO.File.ReadAllBytes(filePath);
			var buffer = WebPDecoder.WebPDecode(webpData, true, out var width, out var height, GetBuffer());

			Width = width;
			Height = height;
			slot.SetResult(buffer);
		}
	}

	public int PoolSize;

	void Update() {
		

		if (_inputQueue.TryTake(out var rgba)) {
			var texture = ImageMath.Views.TextureView.GetByName("Image").ResizeTexture2D(Width, Height, false, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB);
			texture.LoadRawTextureData(rgba);
			ReturnBuffer(rgba);
			PoolSize = bufferPool?.Count ?? 0;
			texture.Apply();

			if (StartTime == 0) {
				StartTime = Time.realtimeSinceStartup;
				FrameCount = 0;
				Fps = 0;
			}

			FrameCount++;
			var elapsed = Time.realtimeSinceStartup - StartTime;
			Fps = FrameCount / elapsed;
		}
	}
	
	public InspectorButton _LoadSingleImage;
	public void LoadSingleImage() {
		if (FilePaths.Length == 0) return;
		var webpData = System.IO.File.ReadAllBytes(FilePaths[0]);
		int width, height;
		var rgba = WebPDecoder.WebPDecode(webpData, true, out width, out height);

		WebPDecoder.WebPDecode(webpData, true, out width, out height, rgba);

		Width = width;
		Height = height;

		var texture = ImageMath.Views.TextureView.GetByName("WebP Test").ResizeTexture2D(Width, Height, false, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB);
		texture.LoadRawTextureData(rgba);
		texture.Apply();
	}



}