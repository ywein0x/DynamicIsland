# Dynamic Island for Windows

iPhone'daki Dynamic Island'ın Windows için açık kaynaklı hali. Ekranın üst ortasında duran, olaylara göre büyüyüp küçülen bir ada. Yaylı animasyonlar ve iOS renkleriyle tasarlandı. C# ve WPF ile yazıldı.

*An open-source take on the iPhone's Dynamic Island for Windows (C#/WPF). The interface is in Turkish.*

## Özellikler

**Ana menü** (adaya tıkla)
- O an olanlar: çalan müzik (önceki, oynat/durdur, sonraki), zamanlayıcı, indirme, Discord; boştayken saat ve sistem durumu
- Hava durumu kartı ve 5 günlük tahmin
- Takvim kartı: Windows takvimi ve adadan eklenen etkinlikler, 10 dakika önce hatırlatma
- En çok kullanılan uygulamalar: gerçek ikonu, kaç saat kullanıldığı ve kaç kez açıldığı, tek tıkla açma; tarayıcılar listede yer almaz, istediğin uygulamayı listeden kaldırabilirsin
- Beğenilen şarkılar: çalan şarkının kalbine basınca listeye eklenir; Spotify hesabı bağlanırsa (Ayarlar › Müzik) Spotify'daki Beğenilenler ile birlikte çalışır

**Müzik:** kapak, kayan şarkı adı, çalan sese göre hareket eden çubuklar, sürükleyerek sarma. YouTube videoları ve Shorts adayı açmaz.

**Discord:** sesli kanal durumu ve süresi, konuşma göstergesi, son mesajlar, kullanım süresi.

**Bildirimler:** uygulama bildirimleri adada görünür, tıklayınca uygulama açılır. Bildirim listesi, Rahatsız Etme modu, uygulama başına açıp kapatma.

**Diğerleri:** zamanlayıcı, ses ve pil göstergeleri, Caps/Num Lock, kamera ve mikrofon uyarısı, pano geçmişi, ekran görüntüsü önizlemesi, indirme takibi, dosya rafı, oyun oturumu özeti.

**Görünüm:** koyu/açık tema, ada boyutu, köşe yuvarlaklığı, vurgu rengi.

Oyunlarda ve tam ekran videolarda tamamen gizlenir. Gösterecek bir şey yokken görünmez; görmek için fareyi ekranın en üst ortasında bekletmek yeterli. Sekmelere yapılan tıklamaları engellemez.

## Kurulum

[Releases](https://github.com/ywein0x/DynamicIsland/releases/latest) sayfasından `DynamicIsland.exe` dosyasını indirip çalıştır. Kendini kurar, Windows ile başlar ve yeni sürüm çıkınca haber verir. Yönetici izni ya da ayrıca .NET kurmak gerekmez. Windows 10 (2004) ve üzeri içindir; Windows 11'de geliştirildi.

Kaldırmak için: Ayarlar › Uygulamalar › Dynamic Island. Kapatmak için gizli simgeler alanındaki ikona sağ tıklayıp Çıkış'ı seç.

## Gizlilik

Hesap yok, reklam yok, kullanım verisi toplanmaz. Her şey bilgisayarında kalır:

- **Bildirimler** Windows'un kendi bildirim kaydından okunur ve bellekte tutulur.
- **En çok kullanılanlar** Windows'un UserAssist kaydından okunur. Sadece Windows'un kaydettiği açılışları ve süreleri gösterir; başka yollarla başlatılan programların sayıları eksik olabilir.
- **Beğenilen şarkılar** sadece bu bilgisayarda, `%LocalAppData%\DynamicIsland\favorites` klasöründe tutulur.
- **Pano geçmişi** sadece bellekte tutulur; şifre yöneticilerinin işaretlediği içerik kaydedilmez.

İnternete sadece şunlar için çıkar: seçtiğin şehrin adıyla hava durumu (Open-Meteo), yeni sürüm denetimi (GitHub) ve Spotify'ı bağladıysan beğeniler (Spotify). Spotify girişi bu bilgisayarda, bu Windows kullanıcısına şifreli olarak tutulur.

## Geliştirme

.NET 8 SDK gerekir.

```bash
dotnet run --project DynamicIsland
```

Tek dosyalık yayın:

```bash
dotnet publish DynamicIsland -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

`bin` klasöründen çalıştırılan geliştirme sürümü kendini kurmaz. Katkılar açıktır: hata bildirimi ve pull request gönderebilirsin.

## Teşekkürler

- [NAudio](https://github.com/naudio/NAudio): ses seviyesi ve ses dalgası
- [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore): bildirim kaydını okuma
- [Open-Meteo](https://open-meteo.com/): hava durumu verisi (CC BY 4.0)

## Lisans

[MIT](LICENSE). Bu proje Apple veya Discord ile bağlantılı değildir; adlar ve markalar sahiplerine aittir.
