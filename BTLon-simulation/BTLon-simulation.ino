#include <Wire.h>
#include <LiquidCrystal_I2C.h>
#include "DHT.h"

// ====== CAU HINH ======
#define DHTPIN 2
#define DHTTYPE DHT11
DHT dht(DHTPIN, DHTTYPE);

LiquidCrystal_I2C lcd(0x3F, 20, 4);

// CHAN DIEU KHIEN
#define LED_PIN 13
#define FAN_PIN 12
#define MOTOR_LED 7
#define LDR_PIN A0
#define SOIL_PIN A2
#define MIST_PIN 11

// NGUONG KIEM SOAT
int lightThreshold = 400;
int tempThreshold = 30;
int soilThreshold = 70;

// ======== CHE DO GIA LAP ========
#define SIMULATION true
unsigned long lastUpdate = 0;
unsigned long lastSerialTime = 0;
const unsigned long serialInterval = 5000;
int simHour = 0;

// GIA TRI BAN DAU
float t = 0;
float h = 0;
int lightValue = 0;
int soilValue = 0;

// ------------------
// DRIFT
// ------------------
float drift(float base, float minV, float maxV, float rate = 0.05) {
  float delta = base * rate;
  float newv = base + random(-delta, delta);
  if (newv < minV) newv = minV;
  if (newv > maxV) newv = maxV;
  return newv;
}

// ------------------------------
// LIGHT CURVE
// ------------------------------
int generateLightByHour(int hour) {
  if (hour < 6 || hour >= 19) return 1;
  float peak = 1023.0;
  float x = hour - 12;
  float max_x = 6;
  float value = peak * (1.0 - (x * x) / (max_x * max_x));
  return (int) drift(value, 0, 1023);
}

// ------------------------------
// SINH DU LIEU THEO GIO
// ------------------------------
void generateByHour(int hNow) {

  lightValue = generateLightByHour(hNow);

  if (hNow >= 0 && hNow < 6) {
    t = drift(t, 18, 26);
    h = drift(h, 75, 95);
    soilValue = drift(soilValue, 60, 90);
  } else if (hNow >= 6 && hNow < 11) {
    t = drift(t, 22, 32);
    h = drift(h, 55, 80);
    soilValue = drift(soilValue, 55, 85);
  } else if (hNow >= 11 && hNow < 16) {
    t = drift(t, 28, 40);
    h = drift(h, 40, 65);
    soilValue = drift(soilValue, 40, 70);
  } else if (hNow >= 16 && hNow < 19) {
    t = drift(t, 24, 33);
    h = drift(h, 50, 75);
    soilValue = drift(soilValue, 50, 80);
  } else {
    t = drift(t, 22, 28);
    h = drift(h, 65, 90);
    soilValue = drift(soilValue, 55, 90);
  }
}

// =======================
// HAM IN TRANG THAI THIET BI (KHONG DAU)
// =======================
void printDeviceState() {
  Serial.print("Quat: ");
  Serial.println((t > tempThreshold) ? "BAT" : "TAT");

  Serial.print("Den: ");
  Serial.println((lightValue < lightThreshold) ? "BAT" : "TAT");

  Serial.print("Bom nuoc: ");
  Serial.println((soilValue < soilThreshold) ? "BAT" : "TAT");

  Serial.print("Phun suong: ");
  Serial.println((h < 60) ? "BAT" : "TAT");
}

// ===============================
//             SETUP
// ===============================
void setup() {
  Serial.begin(9600);
  Serial.println("Connected");

  dht.begin();

  pinMode(LED_PIN, OUTPUT);
  pinMode(FAN_PIN, OUTPUT);
  pinMode(MOTOR_LED, OUTPUT);
  pinMode(MIST_PIN, OUTPUT);

  lcd.init();
  lcd.backlight();

  lcd.setCursor(0,0);
  lcd.print("WELCOME TO");
  lcd.setCursor(0,1);
  lcd.print("SMART GARDEN");
  delay(1000);
  lcd.clear();

  // doc gia tri ban dau
  h = dht.readHumidity();
  t = dht.readTemperature();
  lightValue = analogRead(LDR_PIN);
  soilValue = analogRead(SOIL_PIN);
  soilValue = map(soilValue, 0, 1023, 0, 100);

  // fallback
  if (isnan(h)) h = 75;
  if (isnan(t)) t = 25;
  if (lightValue <= 0) lightValue = 50;
  if (soilValue <= 0 || soilValue > 100) soilValue = 60;

  // Cap nhat trang thai thiet bi truoc khi in gio 0
  digitalWrite(FAN_PIN,      (t > tempThreshold));
  digitalWrite(MIST_PIN,     (h < 60));
  digitalWrite(LED_PIN,      (lightValue < lightThreshold));
  digitalWrite(MOTOR_LED,    (soilValue < soilThreshold));

  // IN HOUR 0
  Serial.println("========================================");
  Serial.println("Hour: 0");
  Serial.print("Nhiet do: "); Serial.println(t);
  Serial.print("Do am khong khi: "); Serial.println(h);

  Serial.print("Anh sang: ");
  Serial.println((lightValue < lightThreshold) ? "Thieu" : "Du");

  Serial.print("Do am dat: ");
  Serial.println(soilValue);

  printDeviceState();

  Serial.println("========================================");
}

// ===============================
//             LOOP
// ===============================
void loop() {

  // mo phong thoi gian moi 10s
  if (SIMULATION) {
    if (millis() - lastUpdate > 10000) {
      simHour++;
      if (simHour > 23) simHour = 0;
      generateByHour(simHour);
      lastUpdate = millis();
    }
  } else {
    h = dht.readHumidity();
    t = dht.readTemperature();
    lightValue = analogRead(LDR_PIN);
    soilValue = analogRead(SOIL_PIN);
    soilValue = map(soilValue, 0, 1023, 0, 100);
  }

  // Cap nhat trang thai thiet bi
  digitalWrite(FAN_PIN,      (t > tempThreshold));
  digitalWrite(MIST_PIN,     (h < 60));
  digitalWrite(LED_PIN,      (lightValue < lightThreshold));
  digitalWrite(MOTOR_LED,    (soilValue < soilThreshold));

  // SERIAL UPDATE moi 10s
  if (millis() - lastSerialTime >= serialInterval) {
    lastSerialTime = millis();
    Serial.println("========================================");
    Serial.print("Gio: "); Serial.println(simHour);

    Serial.print("Nhiet do: "); Serial.println(t);
    Serial.print("Do am khong khi: "); Serial.println(h);

    Serial.print("Anh sang: ");
    Serial.println((lightValue < lightThreshold) ? "Thieu" : "Du");

    Serial.print("Do am dat: ");
    Serial.println(soilValue);

    printDeviceState();

    Serial.println("========================================");
  }

  // LCD
  lcd.setCursor(0,0);
  lcd.print("Nhiet do: ");
  lcd.print(t);
  lcd.print((char)223);
  lcd.print("C   ");

  lcd.setCursor(0,1);
  lcd.print("Do am kk: ");
  lcd.print(h);
  lcd.print("%   ");

  lcd.setCursor(0,2);
  lcd.print("Anh sang: ");
  lcd.print((lightValue < lightThreshold) ? "Thieu   " : "Du      ");

  lcd.setCursor(0,3);
  lcd.print("Do am dat: ");
  lcd.print(soilValue);
  lcd.print("%   ");

  delay(300);
}
