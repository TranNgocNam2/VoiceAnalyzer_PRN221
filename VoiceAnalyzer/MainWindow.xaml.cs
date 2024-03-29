using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using ScottPlot;
using System.DirectoryServices.ActiveDirectory;
using System.IO;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using static System.Windows.Forms.DataFormats;

namespace VoiceAnalyzer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private DispatcherTimer _timer;
        private AudioFileReader? _reader;
        private WasapiOut _wasapiOut;

        private  double[] _audioValues;
        private  double[] _fftValues;
        private  WasapiCapture _audioDevice;

        private WaveIn _waveIn;

        private List<Item> _items;

        private WaveFileWriter _waveFileWriter;
        private string _tempFilePath = "";

        public MainWindow()
        {
            InitializeComponent();
            PopulateDevicesCombo();
            //_audioDevice =  GetSelectedDevice();
            //var format = _audioDevice.WaveFormat;
            //_audioValues = new double[format.SampleRate / 10];
            //var paddedAudio = FftSharp.Pad.ZeroPad(_audioValues);
            //var fftMag = FftSharp.Transform.FFTpower(paddedAudio);
            //_fftValues = new double[fftMag.Length];
            //var fftPeriod = FftSharp.Transform.FFTfreqPeriod(format.SampleRate, fftMag.Length);

            //frequencyPlot.Plot.Add.Signal(_fftValues, 1.0 / fftPeriod);
            //frequencyPlot.Plot.YLabel("Spectral Power");
            //frequencyPlot.Plot.XLabel("Frequency (kHz)");
            //frequencyPlot.Plot.Title($"{format.Encoding} ({format.BitsPerSample}-bit) {format.SampleRate} KHz");
            //frequencyPlot.Plot.Axes.SetLimits(0, 6000, 0, .005);
            //frequencyPlot.Refresh();

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(100);
            _timer.Tick += OnTimerTick!;
            EnableControls(false);
            EnableRecordControls(false);
            ScanFileAndFolders("D:\\Code\\VoiceAnalyzer\\VoiceAnalyzer_PRN221\\VoiceAnalyzer\\Uploads\\");
           
        }

        private void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog();  
            openFileDialog.Filter = "Audio Files (*.mp3;*.wav)|*.mp3;*.wav";

            if (openFileDialog.ShowDialog() == true)
            {
                var filePath = openFileDialog.FileName;
                _reader = new AudioFileReader(filePath);

                var fileName = System.IO.Path.GetFileName(filePath);
                var destination = System.IO.Path.Combine("D:\\Code\\VoiceAnalyzer\\VoiceAnalyzer_PRN221\\VoiceAnalyzer\\Uploads\\", fileName);

                try
                {
                    if (File.Exists(destination))
                    {
                        MessageBox.Show("File already exists at the destination path.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    File.Copy(filePath, destination);
                    ScanFileAndFolders("D:\\Code\\VoiceAnalyzer\\VoiceAnalyzer_PRN221\\VoiceAnalyzer\\Uploads\\");
                    PlayButton_Click(sender, e);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error uploading file: {ex.Message}");
                }

            }
        }
       
        private void OnTimerTick(object sender, EventArgs e)
        {
            var fftPeriod = 0.0;
            double[] paddedAudio;
            double[] fftMag;
            var peakIndex = 0;
            if (_reader is not null)
            {
                paddedAudio = FftSharp.Pad.ZeroPad(_audioValues);
                fftMag = FftSharp.Transform.FFTmagnitude(paddedAudio);

                Array.Copy(fftMag, _fftValues, fftMag.Length);

                peakIndex = 0;

                for (var i = 0; i < fftMag.Length; i++)
                {
                    if (fftMag[i] > fftMag[peakIndex])
                        peakIndex = i;
                }

                fftPeriod = FftSharp.Transform.FFTfreqPeriod(_audioDevice.WaveFormat.SampleRate, fftMag.Length);
                var peakFrequency = fftPeriod * peakIndex;
                lbPeakValue.Content = $"{peakFrequency:N0} Hz";
                frequencyPlot.Refresh();

                textBlockPosition.Text = _reader.CurrentTime.ToString();
                lengthBar.Value = _reader.CurrentTime.TotalSeconds;
            }

            else
            {
                paddedAudio = FftSharp.Pad.ZeroPad(_audioValues);
                fftMag = FftSharp.Transform.FFTmagnitude(paddedAudio);

                Array.Copy(fftMag, _fftValues, fftMag.Length);

                peakIndex = 0;

                for (var i = 0; i < fftMag.Length; i++)
                {
                    if (fftMag[i] > fftMag[peakIndex])
                        peakIndex = i;
                }

                fftPeriod = FftSharp.Transform.FFTfreqPeriod(_audioDevice.WaveFormat.SampleRate, fftMag.Length);
                var peakFrequency = fftPeriod * peakIndex;
                lbPeakValue.Content = $"{peakFrequency:N0} Hz";
                frequencyPlot.Refresh();
            }
        }

        private void EnableControls(bool isPlaying)
        {
            playButton.IsEnabled = !isPlaying;
            stopPlayButton.IsEnabled = isPlaying;
            recordButton.IsEnabled = !isPlaying;
            stopButton.IsEnabled = !isPlaying;
            lengthBar.IsEnabled = isPlaying;
        }

        private void EnableRecordControls(bool isPlaying)
        {
            recordButton.IsEnabled = !isPlaying;
            stopButton.IsEnabled = isPlaying;
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            _timer.Stop();
            lengthBar.Value = 0;
            if (_reader is not null)
            {
                _reader!.Dispose();
                _reader = null;
            }

            if (_wasapiOut is not null)
            {
                _wasapiOut.Dispose();
            }
            EnableControls(false);
            if (e.Exception != null)
            {
                MessageBox.Show(e.Exception.Message);
            }
        }

        private void WaveIn(object sender, WaveInEventArgs e)
        {
            var bytesPerSamplePerChannel = _audioDevice.WaveFormat.BitsPerSample / 8;
            var bytesPerSample = bytesPerSamplePerChannel * _audioDevice.WaveFormat.Channels;
            var bufferSampleCount = e.Buffer.Length / bytesPerSample;

            if (bufferSampleCount >= _audioValues.Length)
            {
                bufferSampleCount = _audioValues.Length;
            }

            if (bytesPerSamplePerChannel == 2 && _audioDevice.WaveFormat.Encoding == WaveFormatEncoding.Pcm)
            {
                for (var i = 0; i < bufferSampleCount; i++)
                    _audioValues[i] = BitConverter.ToInt16(e.Buffer, i * bytesPerSample);
            }
            else if (bytesPerSamplePerChannel == 4 && _audioDevice.WaveFormat.Encoding == WaveFormatEncoding.Pcm)
            {
                for (var i = 0; i < bufferSampleCount; i++)
                    _audioValues[i] = BitConverter.ToInt32(e.Buffer, i * bytesPerSample);
            }
            else if (bytesPerSamplePerChannel == 4 && _audioDevice.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                for (var i = 0; i < bufferSampleCount; i++)
                    _audioValues[i] = BitConverter.ToSingle(e.Buffer, i * bytesPerSample);
            }
            else
            {
                throw new NotSupportedException(_audioDevice.WaveFormat.ToString());
            }

            if (_waveFileWriter is not null)
            {
                _waveFileWriter.Write(e.Buffer, 0, e.BytesRecorded);
            }
        }

        private void ScanFileAndFolders(string path)
        {
            _items = new List<Item>();
            var files = Directory.GetFiles(path);

            foreach (var file in files)
            {
                _items.Add(new Item()
                {
                    Type = System.IO.Path.GetExtension(file),
                    Name = System.IO.Path.GetFileName(file),
                    Path = file,
                });
            }

            listMusic.ItemsSource = _items;
        }

        private void PopulateDevicesCombo()
        {
            var deviceEnumerator = new MMDeviceEnumerator();
            var devices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active);
            foreach (var device in devices)
            {
                comboDevices.Items.Add(device);
            }
            comboDevices.SelectedIndex = 0;
        }

        private WasapiCapture GetSelectedDevice()
        {
            var selectedDevice = (MMDevice) comboDevices.SelectedItem;

            return selectedDevice.DataFlow == DataFlow.Render
                ? new WasapiLoopbackCapture(selectedDevice)
                : new WasapiCapture(selectedDevice, true, 10);
        }


        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            frequencyPlot.Plot.Clear();
            _timer.Start();
            _audioDevice = GetSelectedDevice();
            var format = _audioDevice.WaveFormat;
            _audioValues = new double[format.SampleRate / 10];
            var paddedAudio = FftSharp.Pad.ZeroPad(_audioValues);
            var fftMag = FftSharp.Transform.FFTpower(paddedAudio);
            _fftValues = new double[fftMag.Length];
            var fftPeriod = FftSharp.Transform.FFTfreqPeriod(format.SampleRate, fftMag.Length);

            frequencyPlot.Plot.Add.Signal(_fftValues, 1.0 / fftPeriod);
            frequencyPlot.Plot.YLabel("Spectral Power");
            frequencyPlot.Plot.XLabel("Frequency (kHz)");
            frequencyPlot.Plot.Title($"{format.Encoding} ({format.BitsPerSample}-bit) {format.SampleRate} KHz");
            frequencyPlot.Plot.Axes.SetLimits(0, 50, 0, .005);
            frequencyPlot.Refresh();
            _tempFilePath = Guid.NewGuid().ToString()+".mp3";
            _waveFileWriter = new WaveFileWriter(System.IO.Path.Combine("D:\\Code\\VoiceAnalyzer\\VoiceAnalyzer_PRN221\\VoiceAnalyzer\\Uploads\\", _tempFilePath), format);
            _audioDevice.DataAvailable += WaveIn!;
            _audioDevice.StartRecording();

            EnableRecordControls(true);
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_audioDevice != null)
            {
                _audioDevice.StopRecording();
                _audioDevice.Dispose();
                _audioDevice = null;
            }

            if (_waveFileWriter != null)
            {
                _waveFileWriter.Close();
                _waveFileWriter.Dispose();
                _waveFileWriter = null;
            }

            _timer.Stop();

            frequencyPlot.Plot.Clear();

            ScanFileAndFolders("D:\\Code\\VoiceAnalyzer\\VoiceAnalyzer_PRN221\\VoiceAnalyzer\\Uploads\\");

            EnableRecordControls(false);
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_reader is null) 
            {
                MessageBox.Show("Pick a file first!");
                return;
            }

            var device = (MMDevice)comboDevices.Items[0];
            var sharemode = AudioClientShareMode.Shared;
            var latency = 20;
            var useEventSync = false;
            _wasapiOut = new WasapiOut(device,sharemode, useEventSync,latency);
            _wasapiOut.PlaybackStopped += OnPlaybackStopped!;
            textBlockDuration.Text = _reader.TotalTime.ToString();
            textBlockPosition.Text = _reader.CurrentTime.ToString();
            lengthBar.Maximum = _reader.TotalTime.TotalSeconds;
            lengthBar.Value = 0;
            _timer.Start();
            _wasapiOut.Init(_reader);
            _wasapiOut.Play();
            EnableControls(true);

            _audioDevice = GetSelectedDevice();
            var format = _audioDevice.WaveFormat;
            _audioValues = new double[format.SampleRate / 10];
            var paddedAudio = FftSharp.Pad.ZeroPad(_audioValues);
            var fftMag = FftSharp.Transform.FFTpower(paddedAudio);
            _fftValues = new double[fftMag.Length];
            var fftPeriod = FftSharp.Transform.FFTfreqPeriod(format.SampleRate, fftMag.Length);

            frequencyPlot.Plot.Add.Signal(_fftValues, 1.0 / fftPeriod);
            frequencyPlot.Plot.YLabel("Spectral Power");
            frequencyPlot.Plot.XLabel("Frequency (kHz)");
            frequencyPlot.Plot.Title($"{format.Encoding} ({format.BitsPerSample}-bit) {format.SampleRate} KHz");
            frequencyPlot.Plot.Axes.SetLimits(0, 50, 0, .05);
            frequencyPlot.Refresh();
            _audioDevice.DataAvailable += WaveIn!;
            _audioDevice.StartRecording();
        }

        private void StopPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_reader is not null)
            {
                _wasapiOut.Stop();
                _audioDevice.StopRecording();
                frequencyPlot.Plot.Clear();
            }
        }

        private void lengthBar_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (_reader is not null)
            {
                _reader.CurrentTime = TimeSpan.FromSeconds(lengthBar.Value);
            }
        }

        private void volumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_wasapiOut is not null && _reader is not null)
            {
                _reader.Volume = (float)volumeSlider.Value;
            }
        }

        private void lvMain_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            
            if (listMusic.SelectedItem is not null)
            {
                var selectedItem = (Item)listMusic.SelectedItem;
                _reader = new AudioFileReader(selectedItem.Path);
                PlayButton_Click(sender,e);
            }
        }

        private void ListViewItem_Loaded(object sender, RoutedEventArgs e)
        {

        }

    }


    public class Item
    {
        public string? Type { get; set; }
        public string? Name { get; set; }
        public string? Path { get; set; }
    }
}