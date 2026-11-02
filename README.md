# Dynamic Island for Windows

iPhone'daki Dynamic Island'ın Windows için yapılmış hali. Ekranın üst ortasında duran, olaylara göre büyüyüp küçülen siyah bir ada. Yaylı animasyonlar ve iOS renkleriyle Apple'ın tasarımına benzetildi.

## Özellikler

- **Müzik:** Kapak, kayan şarkı adı, çalan müziğe göre hareket eden ses dalgası, oynat/durdur/ileri/geri ve ilerleme çubuğundan sarma. YouTube videoları ve Shorts adayı açmaz, sadece müzik gösterilir.
- **Bildirimler:** WhatsApp, Discord, Telegram vb. bildirimleri adada görünür, tıklayınca uygulama açılır.
- **Zamanlayıcı:** Turuncu ilerleme halkası ve büyük kart.
- **Sistem:** Ses seviyesi, şarj ve düşük pil, Caps/Num Lock, kamera ve mikrofon kullanımı.
- **Pano ve ekran görüntüsü:** Son 5 kopyalanan metin, ekran alıntısı önizlemesi.
- **İndirmeler:** Tarayıcı indirmelerinin boyutu ve hızı.
- **Dosya rafı:** Dosyaları adaya sürükleyip bırak, sonra istediğin yere sürükle.
- **Oyun özeti:** Oyun kapanınca ne kadar oynadığını gösterir.
- **Sistem durumu:** CPU, RAM ve GPU sıcaklığı.
- **Çift etkinlik, kaydırarak kapatma, öncelik sırası, koyu/açık tema.**

Oyunlarda ve tam ekran videolarda tamamen gizlenir. Gösterecek bir şey yokken görünmez; görmek için fareyi ekranın en üst ortasında bekletmek yeterli.

## Kurulum

[Releases](https://github.com/ywein0x/DynamicIsland/releases/latest) sayfasından `DynamicIsland.exe` dosyasını indirip çalıştırman yeterli. Kendini kurar, Windows ile başlar ve yeni sürüm çıkınca kendini günceller. Yönetici izni ya da ayrıca .NET kurmak gerekmez.

Kaldırmak için: Ayarlar › Uygulamalar › Dynamic Island.

## Geliştirme

.NET 8 SDK gerekir.

```bash
dotnet run --project DynamicIsland
```

Yayın dosyası:

```bash
dotnet publish DynamicIsland -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```
