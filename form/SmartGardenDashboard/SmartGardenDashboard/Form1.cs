using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace SmartGardenDashboard
{
    // ====================== DATA MODELS ======================
    public class SensorData
    {
        public DateTime Timestamp { get; set; }
        public int Hour { get; set; }
        public float Temperature { get; set; }
        public float Humidity { get; set; }
        public string LightStatus { get; set; }
        public int SoilMoisture { get; set; }
        public bool FanStatus { get; set; }
        public bool LedStatus { get; set; }
        public bool PumpStatus { get; set; }
        public bool MistStatus { get; set; }
    }

    // ====================== DATA SERVICE ======================
    public class DataService
    {
        private readonly string dataFilePath;
        private List<SensorData> dataHistory;

        public DataService()
        {
            string appDir = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData), "SmartGarden");
            Directory.CreateDirectory(appDir);
            dataFilePath = Path.Combine(appDir, "sensor_data.csv");
            dataHistory = new List<SensorData>();
            LoadData();
        }

        public void AddData(SensorData data)
        {
            dataHistory.Add(data);
            SaveData(data);
        }

        public List<SensorData> GetAllData() => dataHistory;

        public List<SensorData> GetDataByDateRange(DateTime start, DateTime end)
        {
            return dataHistory.Where(d => d.Timestamp >= start && d.Timestamp <= end).ToList();
        }

        public SensorData GetLatestData()
        {
            return dataHistory.Count > 0 ? dataHistory[dataHistory.Count - 1] : null;
        }

        private void LoadData()
        {
            if (!File.Exists(dataFilePath)) return;

            try
            {
                var lines = File.ReadAllLines(dataFilePath, Encoding.UTF8);
                foreach (var line in lines.Skip(1))
                {
                    var parts = line.Split(',');
                    if (parts.Length >= 9)
                    {
                        dataHistory.Add(new SensorData
                        {
                            Timestamp = DateTime.Parse(parts[0]),
                            Hour = int.Parse(parts[1]),
                            Temperature = float.Parse(parts[2]),
                            Humidity = float.Parse(parts[3]),
                            LightStatus = parts[4],
                            SoilMoisture = int.Parse(parts[5]),
                            FanStatus = bool.Parse(parts[6]),
                            LedStatus = bool.Parse(parts[7]),
                            PumpStatus = bool.Parse(parts[8]),
                            MistStatus = parts.Length > 9 ? bool.Parse(parts[9]) : false
                        });
                    }
                }
            }
            catch { }
        }

        private void SaveData(SensorData data)
        {
            try
            {
                bool fileExists = File.Exists(dataFilePath);
                using (var writer = new StreamWriter(dataFilePath, true, Encoding.UTF8))
                {
                    if (!fileExists)
                        writer.WriteLine("Timestamp,Hour,Temperature,Humidity,Light,SoilMoisture,Fan,Led,Pump,Mist");

                    writer.WriteLine($"{data.Timestamp:yyyy-MM-dd HH:mm:ss},{data.Hour}," +
                        $"{data.Temperature},{data.Humidity},{data.LightStatus},{data.SoilMoisture}," +
                        $"{data.FanStatus},{data.LedStatus},{data.PumpStatus},{data.MistStatus}");
                }
            }
            catch { }
        }
    }

    // ====================== SERIAL SERVICE ======================
    public class SerialService
    {
        private SerialPort serialPort;
        private StringBuilder dataBuffer;
        public event Action<SensorData> DataReceived;
        private SensorData currentData;

        public SerialService()
        {
            dataBuffer = new StringBuilder();
            currentData = new SensorData();
        }

        public string[] GetAvailablePorts() => SerialPort.GetPortNames();

        public bool Connect(string portName)
        {
            try
            {
                serialPort = new SerialPort(portName, 9600);
                serialPort.DataReceived += SerialPort_DataReceived;
                serialPort.Open();
                return true;
            }
            catch { return false; }
        }

        public void Disconnect()
        {
            if (serialPort?.IsOpen == true)
            {
                serialPort.Close();
                serialPort.Dispose();
            }
        }

        public bool IsConnected => serialPort?.IsOpen == true;

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string data = serialPort.ReadExisting();
                dataBuffer.Append(data);

                string buffer = dataBuffer.ToString();
                if (buffer.Contains("========================================"))
                {
                    ParseData(buffer);
                    dataBuffer.Clear();
                }
            }
            catch { }
        }

        private void ParseData(string data)
        {
            try
            {
                var lines = data.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var sensorData = new SensorData { Timestamp = DateTime.Now };

                foreach (var line in lines)
                {
                    if (line.Contains("Gio:") || line.Contains("Hour:"))
                        sensorData.Hour = ExtractInt(line);
                    else if (line.Contains("Nhiet do:"))
                        sensorData.Temperature = ExtractFloat(line);
                    else if (line.Contains("Do am khong khi:"))
                        sensorData.Humidity = ExtractFloat(line);
                    else if (line.Contains("Anh sang:"))
                        sensorData.LightStatus = line.Contains("Thieu") ? "Thiếu" : "Đủ";
                    else if (line.Contains("Do am dat:"))
                        sensorData.SoilMoisture = ExtractInt(line);
                    else if (line.Contains("Quat:"))
                        sensorData.FanStatus = line.Contains("BAT");
                    else if (line.Contains("Den:"))
                        sensorData.LedStatus = line.Contains("BAT");
                    else if (line.Contains("Bom nuoc:"))
                        sensorData.PumpStatus = line.Contains("BAT");
                    else if (line.Contains("Phun suong:"))
                        sensorData.MistStatus = line.Contains("BAT");
                }

                DataReceived?.Invoke(sensorData);
            }
            catch { }
        }

        private int ExtractInt(string line)
        {
            var parts = line.Split(':');
            if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int result))
                return result;
            return 0;
        }

        private float ExtractFloat(string line)
        {
            var parts = line.Split(':');
            if (parts.Length > 1 && float.TryParse(parts[1].Trim(), out float result))
                return result;
            return 0;
        }
    }

    // ====================== MAIN FORM ======================
    public partial class MainForm : Form
    {
        private SerialService serialService;
        private DataService dataService;
        private System.Windows.Forms.Timer updateTimer;

        // Controls
        private ComboBox cmbPorts;
        private Button btnConnect, btnDisconnect;
        private Label lblStatus;
        private Panel pnlCurrentData, pnlDeviceStatus, pnlAlerts;
        private Chart chartTemp, chartHumidity, chartSoil;
        private DateTimePicker dtpStart, dtpEnd;
        private Button btnRefreshChart;
        private DataGridView dgvHistory;

        // Current data labels
        private Label lblTemp, lblHumidity, lblLight, lblSoil;
        private Label lblFan, lblLed, lblPump, lblMist;
        private ListBox lstAlerts;

        public MainForm()
        {
            // Initialize services BEFORE InitializeComponent() since it uses them
            serialService = new SerialService();
            dataService = new DataService();

            InitializeComponent();

            serialService.DataReceived += OnDataReceived;

            updateTimer = new System.Windows.Forms.Timer();
            updateTimer.Interval = 1000;
            updateTimer.Tick += UpdateTimer_Tick;
            updateTimer.Start();

            LoadHistoricalData();
        }

        private void InitializeComponent()
        {
            this.Text = "Smart Garden Dashboard";
            this.Size = new Size(1400, 900);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(240, 240, 240);

            // Top Panel - Connection
            Panel pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
                BackColor = Color.FromArgb(41, 128, 185),
                Padding = new Padding(10)
            };

            Label lblTitle = new Label
            {
                Text = "🌱 SMART GARDEN MONITORING SYSTEM",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(10, 15)
            };

            cmbPorts = new ComboBox
            {
                Location = new Point(500, 18),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            var ports = serialService.GetAvailablePorts();
            if (ports != null && ports.Length > 0)
            {
                cmbPorts.Items.AddRange(ports);
                cmbPorts.SelectedIndex = 0;
            }

            btnConnect = new Button
            {
                Text = "Kết nối",
                Location = new Point(630, 17),
                Width = 80,
                BackColor = Color.FromArgb(46, 204, 113),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnConnect.Click += BtnConnect_Click;

            btnDisconnect = new Button
            {
                Text = "Ngắt",
                Location = new Point(720, 17),
                Width = 80,
                BackColor = Color.FromArgb(231, 76, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };
            btnDisconnect.Click += BtnDisconnect_Click;

            lblStatus = new Label
            {
                Text = "● Chưa kết nối",
                Location = new Point(820, 20),
                AutoSize = true,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };

            pnlTop.Controls.AddRange(new Control[] { lblTitle, cmbPorts, btnConnect, btnDisconnect, lblStatus });
            this.Controls.Add(pnlTop);

            // Main Container
            TableLayoutPanel mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                Padding = new Padding(10)
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            // Left Column - Current Data & Device Status
            CreateCurrentDataPanel();
            mainLayout.Controls.Add(pnlCurrentData, 0, 0);

            CreateDeviceStatusPanel();
            mainLayout.Controls.Add(pnlDeviceStatus, 0, 1);

            // Center Column - Charts
            CreateChartsPanel();
            mainLayout.Controls.Add(chartTemp, 1, 0);
            mainLayout.Controls.Add(chartHumidity, 1, 1);

            // Right Column - Soil Chart & Alerts
            CreateSoilChartPanel();
            mainLayout.Controls.Add(chartSoil, 2, 0);

            CreateAlertsPanel();
            mainLayout.Controls.Add(pnlAlerts, 2, 1);

            this.Controls.Add(mainLayout);

            // Bottom Panel - History
            CreateHistoryPanel();
        }

        private void CreateCurrentDataPanel()
        {
            pnlCurrentData = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Margin = new Padding(5),
                Padding = new Padding(10)
            };

            Label lblTitle = new Label
            {
                Text = "📊 DỮ LIỆU HIỆN TẠI",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Location = new Point(10, 10),
                AutoSize = true,
                ForeColor = Color.FromArgb(52, 73, 94)
            };

            lblTemp = CreateDataLabel("🌡️ Nhiệt độ: -- °C", 50);
            lblHumidity = CreateDataLabel("💧 Độ ẩm KK: -- %", 90);
            lblLight = CreateDataLabel("💡 Ánh sáng: --", 130);
            lblSoil = CreateDataLabel("🌿 Độ ẩm đất: -- %", 170);

            pnlCurrentData.Controls.AddRange(new Control[] { lblTitle, lblTemp, lblHumidity, lblLight, lblSoil });
        }

        private void CreateDeviceStatusPanel()
        {
            pnlDeviceStatus = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Margin = new Padding(5),
                Padding = new Padding(10)
            };

            Label lblTitle = new Label
            {
                Text = "⚙️ TRẠNG THÁI THIẾT BỊ",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Location = new Point(10, 10),
                AutoSize = true,
                ForeColor = Color.FromArgb(52, 73, 94)
            };

            lblFan = CreateDeviceLabel("🌀 Quạt: TẮT", 50, Color.Gray);
            lblLed = CreateDeviceLabel("💡 Đèn: TẮT", 90, Color.Gray);
            lblPump = CreateDeviceLabel("💦 Bơm: TẮT", 130, Color.Gray);
            lblMist = CreateDeviceLabel("🌫️ Phun sương: TẮT", 170, Color.Gray);

            pnlDeviceStatus.Controls.AddRange(new Control[] { lblTitle, lblFan, lblLed, lblPump, lblMist });
        }

        private void CreateChartsPanel()
        {
            // Temperature Chart
            chartTemp = new Chart
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(5),
                BackColor = Color.White
            };

            ChartArea areaTemp = new ChartArea("AreaTemp");
            areaTemp.AxisX.Title = "Thời gian";
            areaTemp.AxisY.Title = "Nhiệt độ (°C)";
            areaTemp.AxisX.LabelStyle.Format = "HH:mm";
            chartTemp.ChartAreas.Add(areaTemp);

            Series seriesTemp = new Series("Temperature")
            {
                ChartType = SeriesChartType.Spline,
                Color = Color.FromArgb(231, 76, 60),
                BorderWidth = 3,
                XValueType = ChartValueType.DateTime
            };
            chartTemp.Series.Add(seriesTemp);

            Title titleTemp = new Title("🌡️ NHIỆT ĐỘ THEO THỜI GIAN")
            {
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 73, 94)
            };
            chartTemp.Titles.Add(titleTemp);

            // Humidity Chart
            chartHumidity = new Chart
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(5),
                BackColor = Color.White
            };

            ChartArea areaHumidity = new ChartArea("AreaHumidity");
            areaHumidity.AxisX.Title = "Thời gian";
            areaHumidity.AxisY.Title = "Độ ẩm (%)";
            areaHumidity.AxisX.LabelStyle.Format = "HH:mm";
            chartHumidity.ChartAreas.Add(areaHumidity);

            Series seriesHumidity = new Series("Humidity")
            {
                ChartType = SeriesChartType.Spline,
                Color = Color.FromArgb(52, 152, 219),
                BorderWidth = 3,
                XValueType = ChartValueType.DateTime
            };
            chartHumidity.Series.Add(seriesHumidity);

            Title titleHumidity = new Title("💧 ĐỘ ẨM KHÔNG KHÍ THEO THỜI GIAN")
            {
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 73, 94)
            };
            chartHumidity.Titles.Add(titleHumidity);
        }

        private void CreateSoilChartPanel()
        {
            chartSoil = new Chart
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(5),
                BackColor = Color.White
            };

            ChartArea areaSoil = new ChartArea("AreaSoil");
            areaSoil.AxisX.Title = "Thời gian";
            areaSoil.AxisY.Title = "Độ ẩm (%)";
            areaSoil.AxisX.LabelStyle.Format = "HH:mm";
            chartSoil.ChartAreas.Add(areaSoil);

            Series seriesSoil = new Series("SoilMoisture")
            {
                ChartType = SeriesChartType.Area,
                Color = Color.FromArgb(100, 46, 204, 113),
                BorderColor = Color.FromArgb(46, 204, 113),
                BorderWidth = 2,
                XValueType = ChartValueType.DateTime
            };
            chartSoil.Series.Add(seriesSoil);

            Title titleSoil = new Title("🌿 ĐỘ ẨM ĐẤT THEO THỜI GIAN")
            {
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 73, 94)
            };
            chartSoil.Titles.Add(titleSoil);
        }

        private void CreateAlertsPanel()
        {
            pnlAlerts = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Margin = new Padding(5),
                Padding = new Padding(10)
            };

            Label lblTitle = new Label
            {
                Text = "⚠️ CẢNH BÁO",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Location = new Point(10, 10),
                AutoSize = true,
                ForeColor = Color.FromArgb(52, 73, 94)
            };

            lstAlerts = new ListBox
            {
                Location = new Point(10, 45),
                Size = new Size(pnlAlerts.Width - 25, pnlAlerts.Height - 60),
                Font = new Font("Segoe UI", 9),
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(255, 250, 240)
            };

            pnlAlerts.Controls.AddRange(new Control[] { lblTitle, lstAlerts });
        }

        private void CreateHistoryPanel()
        {
            Panel pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 250,
                BackColor = Color.White,
                Padding = new Padding(10)
            };

            Label lblTitle = new Label
            {
                Text = "📜 LỊCH SỬ DỮ LIỆU",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Location = new Point(10, 10),
                AutoSize = true
            };

            dtpStart = new DateTimePicker
            {
                Location = new Point(200, 10),
                Width = 150,
                Format = DateTimePickerFormat.Short,
                Value = DateTime.Now.AddDays(-1)
            };

            dtpEnd = new DateTimePicker
            {
                Location = new Point(360, 10),
                Width = 150,
                Format = DateTimePickerFormat.Short
            };

            btnRefreshChart = new Button
            {
                Text = "Làm mới biểu đồ",
                Location = new Point(520, 9),
                Width = 120,
                BackColor = Color.FromArgb(52, 152, 219),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnRefreshChart.Click += BtnRefreshChart_Click;

            dgvHistory = new DataGridView
            {
                Location = new Point(10, 45),
                Size = new Size(pnlBottom.Width - 20, 190),
                ReadOnly = true,
                AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None
            };

            dgvHistory.Columns.Add("Time", "Thời gian");
            dgvHistory.Columns.Add("Hour", "Giờ");
            dgvHistory.Columns.Add("Temp", "Nhiệt độ");
            dgvHistory.Columns.Add("Humidity", "Độ ẩm KK");
            dgvHistory.Columns.Add("Light", "Ánh sáng");
            dgvHistory.Columns.Add("Soil", "Độ ẩm đất");

            pnlBottom.Controls.AddRange(new Control[] { lblTitle, dtpStart, dtpEnd, btnRefreshChart, dgvHistory });
            this.Controls.Add(pnlBottom);
        }

        private Label CreateDataLabel(string text, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(15, y),
                AutoSize = true,
                Font = new Font("Segoe UI", 11),
                ForeColor = Color.FromArgb(52, 73, 94)
            };
        }

        private Label CreateDeviceLabel(string text, int y, Color color)
        {
            return new Label
            {
                Text = text,
                Location = new Point(15, y),
                AutoSize = true,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = color
            };
        }

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            if (cmbPorts.SelectedItem == null)
            {
                MessageBox.Show("Vui lòng chọn cổng COM!", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string portName = cmbPorts.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(portName))
            {
                MessageBox.Show("Cổng COM không hợp lệ!", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (serialService.Connect(portName))
            {
                lblStatus.Text = "● Đã kết nối";
                lblStatus.ForeColor = Color.LightGreen;
                btnConnect.Enabled = false;
                btnDisconnect.Enabled = true;
                MessageBox.Show("Kết nối thành công!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("Không thể kết nối!", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnDisconnect_Click(object sender, EventArgs e)
        {
            serialService.Disconnect();
            lblStatus.Text = "● Chưa kết nối";
            lblStatus.ForeColor = Color.White;
            btnConnect.Enabled = true;
            btnDisconnect.Enabled = false;
        }

        private void OnDataReceived(SensorData data)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => OnDataReceived(data)));
                return;
            }

            if (data == null || dataService == null) return;

            dataService.AddData(data);
            UpdateCurrentData(data);
            UpdateCharts();
            CheckAlerts(data);
            LoadHistoricalData();
        }

        private void UpdateCurrentData(SensorData data)
        {
            if (data == null) return;

            if (lblTemp != null)
                lblTemp.Text = $"🌡️ Nhiệt độ: {data.Temperature:F1} °C";
            if (lblHumidity != null)
                lblHumidity.Text = $"💧 Độ ẩm KK: {data.Humidity:F1} %";
            if (lblLight != null)
                lblLight.Text = $"💡 Ánh sáng: {data.LightStatus ?? "--"}";
            if (lblSoil != null)
                lblSoil.Text = $"🌿 Độ ẩm đất: {data.SoilMoisture} %";

            UpdateDeviceStatus(lblFan, "🌀 Quạt", data.FanStatus);
            UpdateDeviceStatus(lblLed, "💡 Đèn", data.LedStatus);
            UpdateDeviceStatus(lblPump, "💦 Bơm", data.PumpStatus);
            UpdateDeviceStatus(lblMist, "🌫️ Phun sương", data.MistStatus);
        }

        private void UpdateDeviceStatus(Label label, string device, bool status)
        {
            if (label == null) return;
            label.Text = $"{device}: {(status ? "BẬT" : "TẮT")}";
            label.ForeColor = status ? Color.FromArgb(46, 204, 113) : Color.Gray;
        }

        private void UpdateCharts()
        {
            var data = dataService?.GetDataByDateRange(dtpStart.Value, dtpEnd.Value);
            if (data == null) return;

            if (chartTemp?.Series != null && chartTemp.Series.Count > 0)
                chartTemp.Series[0].Points.Clear();
            if (chartHumidity?.Series != null && chartHumidity.Series.Count > 0)
                chartHumidity.Series[0].Points.Clear();
            if (chartSoil?.Series != null && chartSoil.Series.Count > 0)
                chartSoil.Series[0].Points.Clear();

            foreach (var item in data)
            {
                if (chartTemp?.Series != null && chartTemp.Series.Count > 0)
                    chartTemp.Series[0].Points.AddXY(item.Timestamp, item.Temperature);
                if (chartHumidity?.Series != null && chartHumidity.Series.Count > 0)
                    chartHumidity.Series[0].Points.AddXY(item.Timestamp, item.Humidity);
                if (chartSoil?.Series != null && chartSoil.Series.Count > 0)
                    chartSoil.Series[0].Points.AddXY(item.Timestamp, item.SoilMoisture);
            }
        }

        private void CheckAlerts(SensorData data)
        {
            if (lstAlerts == null || data == null) return;

            lstAlerts.Items.Clear();

            if (data.Temperature > 35)
                lstAlerts.Items.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ Nhiệt độ quá cao: {data.Temperature}°C");

            if (data.Temperature < 15)
                lstAlerts.Items.Add($"[{DateTime.Now:HH:mm:ss}] ❄️ Nhiệt độ quá thấp: {data.Temperature}°C");

            if (data.Humidity < 40)
                lstAlerts.Items.Add($"[{DateTime.Now:HH:mm:ss}] 💧 Độ ẩm KK thấp: {data.Humidity}%");

            if (data.SoilMoisture < 50)
                lstAlerts.Items.Add($"[{DateTime.Now:HH:mm:ss}] 🌿 Đất khô, cần tưới: {data.SoilMoisture}%");

            if (data.LightStatus == "Thiếu")
                lstAlerts.Items.Add($"[{DateTime.Now:HH:mm:ss}] 💡 Thiếu ánh sáng");

            if (lstAlerts.Items.Count == 0)
                lstAlerts.Items.Add($"[{DateTime.Now:HH:mm:ss}] ✅ Hệ thống hoạt động bình thường");
        }

        private void LoadHistoricalData()
        {
            if (dgvHistory == null || dataService == null) return;

            dgvHistory.Rows.Clear();
            var data = dataService.GetDataByDateRange(dtpStart.Value, dtpEnd.Value);
            if (data == null) return;

            // Use Skip and Take for compatibility (TakeLast is .NET 6+)
            int skipCount = Math.Max(0, data.Count - 50);
            foreach (var item in data.Skip(skipCount).Take(50))
            {
                dgvHistory.Rows.Add(
                    item.Timestamp.ToString("dd/MM/yyyy HH:mm:ss"),
                    item.Hour,
                    $"{item.Temperature:F1}°C",
                    $"{item.Humidity:F1}%",
                    item.LightStatus ?? "",
                    $"{item.SoilMoisture}%"
                );
            }
        }

        private void BtnRefreshChart_Click(object sender, EventArgs e)
        {
            UpdateCharts();
            LoadHistoricalData();
            MessageBox.Show("Đã cập nhật biểu đồ!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            // Update status periodically
            if (serialService.IsConnected)
            {
                lblStatus.Text = $"● Đã kết nối - {DateTime.Now:HH:mm:ss}";
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            serialService.Disconnect();
            base.OnFormClosing(e);
        }
    }

    // ====================== PROGRAM ENTRY ======================
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}