using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Netling.Core;
using Netling.Core.HttpClientWorker;
using Netling.Core.Models;
using Netling.Core.SocketWorker;

using Newtonsoft.Json;

namespace Netling.Client
{
    public partial class MainWindow
    {
        private bool _running;
        private CancellationTokenSource _cancellationTokenSource;
        private Task<WorkerResult> _task;
        private List<ResultWindow> _resultWindows;
        private ResultWindowItem _baselineResult;
        private Dictionary<string, string> _headers;

        public MainWindow()
        {
            _resultWindows = new List<ResultWindow>();
            _headers = new Dictionary<string, string>();
            InitializeComponent();
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en");
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en");
            Thread.CurrentThread.CurrentCulture.NumberFormat.NumberGroupSeparator = " ";
            Loaded += OnLoaded;
        }

        private AuthInfo authInfo = File.Exists(AuthHelper.InputFile) ? JsonConvert.DeserializeObject<AuthInfo>(File.ReadAllText(AuthHelper.InputFile)) : new AuthInfo { ApiScopes = new List<string>(), Endpoints = new List<string>() };

        private void OnLoaded(object sender, RoutedEventArgs routedEventArgs)
        {
            Threads.SelectedValuePath = "Key";
            Threads.DisplayMemberPath = "Value";

            for (var i = 1; i <= Environment.ProcessorCount; i++)
            {
                Threads.Items.Add(new KeyValuePair<int, string>(i, i.ToString()));
            }

            for (var i = 2; i <= 20; i++)
            {
                Threads.Items.Add(new KeyValuePair<int, string>(Environment.ProcessorCount * i, $"{Environment.ProcessorCount * i} - ({i} per core)"));
            }

            Threads.SelectedIndex = 7;
            Duration.SelectedIndex = 0;
            LoadInput();
            Url.Focus();
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_running)
            {
                var duration = default(TimeSpan);
                int? count = null;
                var threads = Convert.ToInt32(((KeyValuePair<int, string>)Threads.SelectionBoxItem).Key);
                var durationText = (string)((ComboBoxItem)Duration.SelectedItem).Content;
                StatusProgressbar.IsIndeterminate = false;

                switch (durationText)
                {
                    case "10 seconds":
                        duration = TimeSpan.FromSeconds(10);
                        break;
                    case "20 seconds":
                        duration = TimeSpan.FromSeconds(20);
                        break;
                    case "1 minute":
                        duration = TimeSpan.FromMinutes(1);
                        break;
                    case "10 minutes":
                        duration = TimeSpan.FromMinutes(10);
                        break;
                    case "1 hour":
                        duration = TimeSpan.FromHours(1);
                        break;
                    case "Until canceled":
                        duration = TimeSpan.MaxValue;
                        StatusProgressbar.IsIndeterminate = true;
                        break;
                    case "1 run on 1 thread":
                        count = 1;
                        StatusProgressbar.IsIndeterminate = true;
                        break;
                    case "100 runs on 1 thread":
                        count = 100;
                        StatusProgressbar.IsIndeterminate = true;
                        break;
                    case "1000 runs on 1 thread":
                        count = 1000;
                        StatusProgressbar.IsIndeterminate = true;
                        break;
                    case "3000 runs on 1 thread":
                        count = 3000;
                        StatusProgressbar.IsIndeterminate = true;
                        break;
                    case "10000 runs on 1 thread":
                        count = 10000;
                        StatusProgressbar.IsIndeterminate = true;
                        break;

                }

                if (string.IsNullOrWhiteSpace(Url.Text))
                {
                    return;
                }

                if (!Uri.TryCreate(Url.Text.Trim(), UriKind.Absolute, out var uri))
                {
                    return;
                }

                Threads.IsEnabled = false;
                Duration.IsEnabled = false;
                Url.IsEnabled = false;
                StartButton.Content = "Cancel";
                _running = true;

                _cancellationTokenSource = new CancellationTokenSource();
                var cancellationToken = _cancellationTokenSource.Token;

                await LoadHeaders();

                StatusProgressbar.Value = 0;
                StatusProgressbar.Visibility = Visibility.Visible;

                var worker = new Worker(new HttpClientWorkerJob(uri, _headers)); // SocketWorkerJob

                if (count.HasValue)
                {
                    _task = worker.Run(uri.ToString(), count.Value, cancellationToken);
                }
                else
                {
                    _task = worker.Run(uri.ToString(), threads, duration, cancellationToken);
                }

                _task.GetAwaiter().OnCompleted(async () =>
                {
                    await JobCompleted();
                });

                if (StatusProgressbar.IsIndeterminate)
                {
                    return;
                }

                var sw = Stopwatch.StartNew();

                while (!cancellationToken.IsCancellationRequested && duration.TotalMilliseconds > sw.Elapsed.TotalMilliseconds)
                {
                    await Task.Delay(500);
                    StatusProgressbar.Value = 100.0 / duration.TotalMilliseconds * sw.Elapsed.TotalMilliseconds;
                }

                if (!_running)
                {
                    return;
                }

                StatusProgressbar.IsIndeterminate = true;
                StartButton.IsEnabled = false;
            }
            else
            {
                if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
                {
                    _cancellationTokenSource.Cancel();
                }
            }
        }

        private void Urls_OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Return)
            {
                return;
            }

            StartButton_Click(sender, null);
            StartButton.Focus();
        }

        private async Task JobCompleted()
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
            }

            _running = false;
            Threads.IsEnabled = true;
            Duration.IsEnabled = true;
            Url.IsEnabled = true;
            StartButton.IsEnabled = false;
            _cancellationTokenSource = null;

            var result = new ResultWindow(this);
            result.Closing += ResultWindowClosing;
            _resultWindows.Add(result);
            await result.Load(_task.Result, _baselineResult);
            result.Show();
            _task = null;
            StatusProgressbar.Visibility = Visibility.Hidden;
            StartButton.IsEnabled = true;
            StartButton.Content = "Run";
        }

        private void ResultWindowClosing(object sender, EventArgs e)
        {
            _resultWindows.Remove((ResultWindow)sender);
        }

        public void SetBaseline(ResultWindowItem baselineResult)
        {
            _baselineResult = baselineResult;

            foreach (var resultWindow in _resultWindows)
            {
                if (baselineResult != null)
                {
                    resultWindow.LoadBaseline(baselineResult);
                }
                else
                {
                    resultWindow.ClearBaseline();
                }
            }
        }

        private void ClearHeaders_Click(object sender, RoutedEventArgs e)
        {
            LoadInput(true);
        }

        private async Task LoadHeaders()
        {
            SaveInput();
            _headers.Clear();
            this.TraceId.Text = Guid.NewGuid().ToString();
            Debug.WriteLine($"{nameof(this.TraceId)}: {this.TraceId.Text}");
            if (!string.IsNullOrWhiteSpace(this.authInfo.UserId) && !string.IsNullOrWhiteSpace(this.authInfo.Password))
            {
                var token = await AuthHelper.GetAuthTokenSilentAsync(this.authInfo);
                _headers.Add("Authorization", "Bearer " + token);
            }

            _headers.Add("Request_Id", this.TraceId.Text);
            _headers.Add("operation_Id", this.TraceId.Text);
        }

        private void LoadInput(bool clear = false)
        {
            this.Url.Text = clear ? string.Empty : this.authInfo.Endpoints.FirstOrDefault();
            this.Authority.Text = clear ? string.Empty : this.authInfo.Authority;
            this.ClientId.Text = clear ? string.Empty : this.authInfo.ClientId;
            this.ApiScopes.Text = clear ? string.Empty : string.Join(',', this.authInfo.ApiScopes);
            this.UserId.Text = clear ? string.Empty : this.authInfo.UserId;
            this.Password.Password = clear ? string.Empty : this.authInfo.Password;
        }

        private void SaveInput()
        {
            this.authInfo.Endpoints = this.Url.Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            this.authInfo.Authority = this.Authority.Text;
            this.authInfo.ClientId = this.ClientId.Text;
            this.authInfo.ApiScopes = this.ApiScopes.Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            this.authInfo.UserId = this.UserId.Text;
            this.authInfo.Password = this.Password.Password;

            File.WriteAllText(AuthHelper.InputFile, JsonConvert.SerializeObject(this.authInfo));
        }
    }
}
