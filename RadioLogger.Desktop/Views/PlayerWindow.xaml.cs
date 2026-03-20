using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using RadioLogger.ViewModels;

namespace RadioLogger.Views
{
    public partial class PlayerWindow : Window
    {
        private bool _isDragging;
        private FrameworkElement? _dragSource;

        // Zoom state
        private double _zoomLevel = 1.0;
        private double _viewOffset = 0.0;
        private const double MaxZoom = 128.0;
        private const double MinZoom = 1.0;

        private double VisibleWidth => 1.0 / _zoomLevel;

        public PlayerWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            VizBorder.SizeChanged += (_, _) => { RedrawWaveform(); UpdatePlayheadPosition(); RenderTimeline(); };
            TimelineBorder.SizeChanged += (_, _) => RenderTimeline();
            VizBorder.MouseWheel += VizBorder_MouseWheel;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is INotifyPropertyChanged oldVm)
                oldVm.PropertyChanged -= Vm_PropertyChanged;
            if (e.NewValue is INotifyPropertyChanged newVm)
                newVm.PropertyChanged += Vm_PropertyChanged;
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlayerViewModel.CurrentPosition) && !_isDragging)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    UpdatePlayheadPosition();
                    AutoScrollToPlayhead();
                });
            }
            else if (e.PropertyName == nameof(PlayerViewModel.TotalDuration))
            {
                Dispatcher.BeginInvoke(() => { ResetZoom(); UpdatePlayheadPosition(); RenderTimeline(); });
            }
            else if (e.PropertyName == nameof(PlayerViewModel.IsConcatenatedMode))
            {
                Dispatcher.BeginInvoke(() =>
                {
                    ResetZoom(); UpdatePlayheadPosition(); RenderTimeline();
                    // Show/hide join button based on current selection
                    int selCount = RecordingsListBox.SelectedItems.Count;
                    bool showJoin = selCount >= 2 && DataContext is PlayerViewModel vm3 && !vm3.IsConcatenatedMode;
                    JoinButton.Visibility = showJoin ? Visibility.Visible : Visibility.Collapsed;
                });
            }
            else if (e.PropertyName == nameof(PlayerViewModel.IsGeneratingVisualization))
            {
                // When waveform generation finishes, re-render at actual display size
                if (DataContext is PlayerViewModel vm2 && !vm2.IsGeneratingVisualization)
                    Dispatcher.BeginInvoke(() => { RedrawWaveform(); UpdatePlayheadPosition(); });
            }
        }

        // ─── ZOOM ────────────────────────────────────────────────

        private void VizBorder_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (DataContext is not PlayerViewModel vm) return;

            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                // Shift + Scroll = Pan
                double panAmount = 0.1 / _zoomLevel;
                if (e.Delta > 0) _viewOffset -= panAmount;
                else _viewOffset += panAmount;
                ClampViewOffset();
            }
            else
            {
                // Scroll = Zoom centered on mouse
                double mouseX = e.GetPosition(VizBorder).X;
                double mouseRatio = mouseX / VizBorder.ActualWidth;
                double audioPoint = _viewOffset + mouseRatio * VisibleWidth;

                if (e.Delta > 0) _zoomLevel = Math.Min(MaxZoom, _zoomLevel * 1.4);
                else _zoomLevel = Math.Max(MinZoom, _zoomLevel / 1.4);

                _viewOffset = audioPoint - mouseRatio * VisibleWidth;
                ClampViewOffset();
            }

            RedrawWaveform();
            UpdatePlayheadPosition();
            RenderTimeline();
            UpdateZoomLabel();
            e.Handled = true;
        }

        private void ClampViewOffset()
        {
            double maxOffset = Math.Max(0, 1.0 - VisibleWidth);
            _viewOffset = Math.Clamp(_viewOffset, 0, maxOffset);
        }

        private void ResetZoom()
        {
            _zoomLevel = 1.0;
            _viewOffset = 0.0;
            UpdateZoomLabel();
            RedrawWaveform();
        }

        private void RedrawWaveform()
        {
            if (DataContext is not PlayerViewModel vm) return;
            int w = (int)Math.Max(100, VizBorder.ActualWidth);
            int h = (int)Math.Max(50, VizBorder.ActualHeight);
            vm.RenderWaveformRegion(_viewOffset, _viewOffset + VisibleWidth, w, h);
        }

        private void UpdateZoomLabel()
        {
            if (_zoomLevel > 1.01)
                ZoomLevelText.Text = $"x{_zoomLevel:F0}";
            else
                ZoomLevelText.Text = "";
        }

        private void AutoScrollToPlayhead()
        {
            if (DataContext is not PlayerViewModel vm) return;
            if (_zoomLevel <= 1.0) return;
            if (vm.TotalDuration <= 0) return;

            double posRatio = vm.CurrentPosition / vm.TotalDuration;
            double visEnd = _viewOffset + VisibleWidth;

            if (posRatio < _viewOffset || posRatio > visEnd)
            {
                _viewOffset = posRatio - VisibleWidth * 0.1;
                ClampViewOffset();
                RedrawWaveform();
                RenderTimeline();
            }
        }

        private void ResetZoom_Click(object sender, RoutedEventArgs e)
        {
            ResetZoom();
            UpdatePlayheadPosition();
            RenderTimeline();
        }

        // ─── PLAYHEAD ────────────────────────────────────────────

        private void UpdatePlayheadPosition()
        {
            if (DataContext is not PlayerViewModel vm) return;

            // Hide playhead when no audio is loaded
            if (vm.TotalDuration <= 0 || vm.WaveformBitmap == null)
            {
                Playhead.Opacity = 0;
                return;
            }

            double width = VizBorder.ActualWidth;
            double posRatio = vm.CurrentPosition / vm.TotalDuration;

            double x;
            if (_zoomLevel > 1.0)
            {
                double localRatio = (posRatio - _viewOffset) / VisibleWidth;
                x = localRatio * width - 7;
            }
            else
            {
                x = posRatio * width - 7;
            }

            Canvas.SetLeft(Playhead, x);
            Playhead.Height = VizBorder.ActualHeight;
            Playhead.Opacity = (x > -14 && x < width) ? 1.0 : 0.0;
        }

        // ─── TIMELINE ────────────────────────────────────────────

        private void RenderTimeline()
        {
            TimelineCanvas.Children.Clear();
            if (DataContext is not PlayerViewModel vm) return;
            if (vm.TotalDuration <= 0) return;

            double width = TimelineBorder.ActualWidth;
            double height = TimelineBorder.ActualHeight;
            if (width <= 0 || height <= 0) return;

            double totalSec = vm.TotalDuration;
            double startSec = _viewOffset * totalSec;
            double endSec = (_viewOffset + VisibleWidth) * totalSec;
            double visibleDuration = endSec - startSec;

            double majorInterval;
            if (visibleDuration <= 2) majorInterval = 0.5;
            else if (visibleDuration <= 5) majorInterval = 1;
            else if (visibleDuration <= 15) majorInterval = 2;
            else if (visibleDuration <= 30) majorInterval = 5;
            else if (visibleDuration <= 60) majorInterval = 10;
            else if (visibleDuration <= 300) majorInterval = 30;
            else if (visibleDuration <= 900) majorInterval = 60;
            else if (visibleDuration <= 3600) majorInterval = 300;
            else majorInterval = 600;

            double minorInterval = majorInterval / 5;

            double firstMinor = Math.Ceiling(startSec / minorInterval) * minorInterval;
            double firstMajor = Math.Ceiling(startSec / majorInterval) * majorInterval;

            var minorBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            var majorBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            var labelBrush = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
            var consolasFont = new FontFamily("Consolas");
            minorBrush.Freeze(); majorBrush.Freeze(); labelBrush.Freeze();

            for (double t = firstMinor; t <= endSec; t += minorInterval)
            {
                double x = ((t - startSec) / visibleDuration) * width;
                TimelineCanvas.Children.Add(new Line
                {
                    X1 = x, X2 = x, Y1 = height - 4, Y2 = height,
                    Stroke = minorBrush, StrokeThickness = 1
                });
            }

            for (double t = firstMajor; t <= endSec; t += majorInterval)
            {
                double x = ((t - startSec) / visibleDuration) * width;

                TimelineCanvas.Children.Add(new Line
                {
                    X1 = x, X2 = x, Y1 = height - 8, Y2 = height,
                    Stroke = majorBrush, StrokeThickness = 1
                });

                var ts = TimeSpan.FromSeconds(t);
                string label;
                if (visibleDuration <= 5)
                    label = $"{ts.Minutes}:{ts.Seconds:D2}.{ts.Milliseconds / 100}";
                else if (ts.TotalHours >= 1)
                    label = $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
                else
                    label = $"{ts.Minutes}:{ts.Seconds:D2}";

                var tb = new TextBlock
                {
                    Text = label, Foreground = labelBrush,
                    FontSize = 9, FontFamily = consolasFont
                };
                Canvas.SetLeft(tb, x + 3);
                Canvas.SetTop(tb, 1);
                TimelineCanvas.Children.Add(tb);
            }
        }

        // ─── CLICK & DRAG SEEK ───────────────────────────────────

        private void VizBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not PlayerViewModel vm) return;
            if (vm.TotalDuration <= 0) return;

            var source = (FrameworkElement)sender;
            _isDragging = true;
            _dragSource = source;
            vm.BeginSeek();

            SeekToMouse(e, source);

            source.CaptureMouse();
            source.MouseMove += DragSource_MouseMove;
            source.MouseLeftButtonUp += DragSource_MouseLeftButtonUp;
        }

        private void DragSource_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _dragSource == null) return;
            SeekToMouse(e, _dragSource);
        }

        private void DragSource_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging || _dragSource == null) return;

            _dragSource.MouseMove -= DragSource_MouseMove;
            _dragSource.MouseLeftButtonUp -= DragSource_MouseLeftButtonUp;
            _dragSource.ReleaseMouseCapture();

            _isDragging = false;
            _dragSource = null;
            if (DataContext is PlayerViewModel vm) vm.EndSeek();
        }

        private void SeekToMouse(MouseEventArgs e, FrameworkElement relativeTo)
        {
            if (DataContext is not PlayerViewModel vm) return;

            double x = e.GetPosition(relativeTo).X;
            double width = relativeTo.ActualWidth;
            double localRatio = Math.Clamp(x / width, 0, 1);

            double globalRatio;
            if (_zoomLevel > 1.0)
            {
                globalRatio = _viewOffset + localRatio * VisibleWidth;
                globalRatio = Math.Clamp(globalRatio, 0, 1);
            }
            else
            {
                globalRatio = localRatio;
            }

            vm.CurrentPosition = globalRatio * vm.TotalDuration;
            UpdatePlayheadPosition();
        }

        // ─── FILE SELECTION & CONCATENATION ─────────────────────

        private void RecordingsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var listBox = (System.Windows.Controls.ListBox)sender;
            int count = listBox.SelectedItems.Count;

            // Show join button when 2+ files selected and not in concatenated mode
            bool showJoin = count >= 2 && DataContext is PlayerViewModel vm2 && !vm2.IsConcatenatedMode;
            JoinButton.Visibility = showJoin ? Visibility.Visible : Visibility.Collapsed;

            // Single selection → load file for playback
            if (count == 1 && listBox.SelectedItem is System.IO.FileSystemInfo file)
            {
                if (DataContext is PlayerViewModel vm && vm.SelectedRecording != file)
                {
                    // If in concatenated mode, discard first
                    if (vm.IsConcatenatedMode) vm.DiscardConcatenation();
                    vm.SelectedRecording = file;
                }
            }
        }

        private async void JoinFiles_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not PlayerViewModel vm) return;

            var selected = RecordingsListBox.SelectedItems
                .Cast<System.IO.FileSystemInfo>()
                .ToList();

            if (selected.Count < 2) return;

            JoinButton.IsEnabled = false;
            ResetZoom();
            await vm.ConcatenateFilesAsync(selected);
            JoinButton.IsEnabled = true;
            JoinButton.Visibility = Visibility.Collapsed;
        }

        private void DeleteFiles_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not PlayerViewModel vm) return;

            var selected = RecordingsListBox.SelectedItems
                .Cast<System.IO.FileSystemInfo>()
                .ToList();

            if (selected.Count == 0) return;

            string msg = selected.Count == 1
                ? $"¿Eliminar \"{selected[0].Name}\"?"
                : $"¿Eliminar {selected.Count} archivos seleccionados?";

            var result = System.Windows.MessageBox.Show(msg, "Confirmar eliminación",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            // Stop playback if any selected file is currently playing
            vm.Stop();

            int deleted = 0;
            foreach (var file in selected)
            {
                try
                {
                    System.IO.File.Delete(file.FullName);
                    deleted++;
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"No se pudo eliminar \"{file.Name}\":\n{ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            if (deleted > 0)
                vm.RefreshFileList();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not PlayerViewModel vm) return;

            var selected = RecordingsListBox.SelectedItems
                .Cast<System.IO.FileSystemInfo>()
                .FirstOrDefault();

            if (selected == null) return;

            string folder = System.IO.Path.GetDirectoryName(selected.FullName) ?? "";
            if (!string.IsNullOrEmpty(folder))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{selected.FullName}\"");
        }

        // ─── CLEANUP ─────────────────────────────────────────────

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (DataContext is INotifyPropertyChanged npc)
                npc.PropertyChanged -= Vm_PropertyChanged;
            if (DataContext is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
