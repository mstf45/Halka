# Halka

Windows’ta çalışan kişisel dosya dönüştürücü. Görseller, PDF, Word ve Excel dosyaları bu bilgisayarda dönüşür. Bir web sayfasını PDF olarak kaydedebilir, bir video bağlantısından istediğin çözünürlük veya ses biçimini indirebilirsin.

Dosya dönüşümü ağ kullanmaz. Ağ yalnızca iki yerde vardır: sayfayı PDF kaydetmek ve bir bağlantıdan video veya ses indirmek. İndirme, resmi [yt-dlp](https://github.com/yt-dlp/yt-dlp) ile yapılır. Hesap çerezi alınmaz, oturum açılmaz.

## Gerekenler

- Windows 10 veya Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (derlemek için)
- Sayfayı PDF kaydetmek için [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/). Windows 11’de Edge ile birlikte gelir.
- YouTube bağlantıları için [Node.js](https://nodejs.org/). Uygulama `C:\Program Files\nodejs\node.exe` yoluna bakar.
- Video birleştirme ve MP3 için FFmpeg. İlk ihtiyaçta sorulur ve resmi yt-dlp FFmpeg paketinden indirilir. Kendi `ffmpeg.exe` dosyanı da gösterebilirsin.

Kurulum sihirbazı yoktur. Derlenen `Halka.exe` yeterlidir.

## Kurulum

```powershell
git clone https://github.com/mstf45/Halka.git
cd Halka
dotnet build Halka\Halka.csproj -c Release
```

Çalıştır:

```powershell
.\Halka\bin\Release\net8.0-windows\Halka.exe
```

Testler:

```powershell
dotnet test Halka.Tests\Halka.Tests.csproj -c Release
```

## Dosya dönüştürme

1. Pencereye dosyayı sürükle veya **Dosya seç** ile birden fazla dosya seç.
2. Açılan halkadan biçimi seç. Birden fazla görselde **Tek PDF** dilimi de çıkar.
3. İşlem bitince “Dönüşen dosyalar nereye kaydedilsin?” sorusu gelir. Seçtiğin klasöre kaydedilir. Vazgeçersen sonuç silinir.

Ctrl basılıyken bir dosyayı yaklaşık 18 piksel sürükleyince halka imlecin yanında açılır. Tekerlek veya Tab arka yüzü çevirir. Bıraktığın dilim dönüşümü başlatır. Bu davranış **Ctrl ile sürükleyince halkayı aç** kutusundan kapatılabilir.

Pencereyi kapatmak uygulamayı kapatmaz; simge tepside kalır. Tepsi menüsünden tekrar açabilir veya tamamen çıkabilirsin.

| Dosya | Halka |
| --- | --- |
| PNG, JPG, WEBP, BMP, GIF, HEIC, HEIF | PNG, JPG, WEBP, PDF, Tek PDF, BMP, GIF, Gri, Küçük, ZIP |
| PDF | PNG, JPG, WEBP, Birleştir, ZIP |
| DOCX | TXT |
| XLSX | CSV |
| ZIP | Çıkar |
| Klasör | ZIP |
| Video (FFmpeg varsa) | MP4, GIF, MP3, WEBM, ZIP |
| Ses (FFmpeg varsa) | MP3, WAV, FLAC, OGG, ZIP |

HEIC ve HEIF okunur, başka biçime çevrilir. HEIC olarak geri yazılmaz. Görselde piksel sınırı 80 milyondur. PDF ve Office dosyalarında boyut sınırı 200 MB, PDF sayfa sınırı 300’dür.

## Bağlantı

Yeşil kutuya `http` veya `https` adresi yapıştır.

- **Biçimleri getir:** Video ve Ses sekmelerinde çözünürlük, uzantı ve boyut listelenir. **İndir** seçtiğin satırı `İndirilenler\Halka` klasörüne kaydeder.
- **Sayfayı PDF:** Sayfayı açar ve aynı klasöre PDF yazar. İndirme ve yeni pencere engellenir.
- **Vazgeç:** Süren işi durdurur.

İlk video listesinde resmi `yt-dlp.exe` bir kez indirilir ve `%APPDATA%\Halka\tools` altına konur. 1080p veya MP3 gibi birleştirme isteyen satırlar FFmpeg ister; yoksa indirme onayı çıkar. Oynatma listesi indirilmez, yalnızca yapıştırılan tek kayıt alınır.

Yalnızca indirmeye hakkın olan içeriği kaydet.

## Ayarlar

Pencerenin altındaki kutular hemen kaydedilir. Dosya `%APPDATA%\Halka\settings.json` içindedir ve bu depoya girmez.

- JPG / WEBP kalitesi
- FFmpeg yolu. Boşsa gereken indirmede kurulur.
- Ctrl ile sürükleyince halkayı aç
- Windows açılınca başlat
- Açılışta bu pencereyi göster

Son dönüşümler listesinde bir satıra çift tıklamak dosyanın klasörünü açar.

## Komut satırı

Komut satırı, sonucu kaynak dosyanın yanına yazar. Klasör sormaz.

```powershell
Halka help
Halka convert dosya.png --to webp
Halka convert a.png b.png --to pdf --merge
Halka convert belge.docx --to txt
Halka convert tablo.xlsx --to csv
Halka convert arsiv.zip --to extract
Halka convert video.mp4 --to mp3 --ffmpeg C:\tools\ffmpeg.exe
```

`--quality` JPG ve WEBP için 1 ile 100 arasındadır. Varsayılan 85’tir.

## Bu sürümde olmayanlar

HWP, CAD, e-kitap ve yüzlerce ofis biçimi yoktur. İzlenen klasör, kırpma ve Gezgin sağ tık menüsü yoktur. Otomatik güncelleme ve kullanım istatistiği gönderilmez.
