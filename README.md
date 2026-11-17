# Coding Flashcards

Aplikasi desktop Windows untuk persiapan interview coding, diadaptasi dari QuizFlashcards. Menggunakan C# / WPF / .NET 10, SQLite lokal, dan KelontongAI. Antarmuka dan pembahasan berbahasa Indonesia.

## Menjalankan

Paket terbaru dengan dua mode bahasa tersedia di **`artifacts/publish-modes/CodingFlashcards.exe`**. Folder ini dipakai karena versi lama sedang mengunci DLL di `artifacts/publish`. Setelah versi lama ditutup, jalankan `scripts/build.ps1 -Publish` untuk memperbarui lokasi standar.

Lokasi publish standar adalah **`artifacts/publish/CodingFlashcards.exe`**. Seluruh isi folder publish harus tetap bersama; runtime .NET sudah disertakan.

1. Buka Settings. Key dari environment Windows `kelontongai` diimpor saat pertama kali dibuka. Anda juga bisa mengetik key atau mengimpornya ulang. Key tersimpan memakai Windows DPAPI CurrentUser.
2. Centang bahasa latihan: Python, JavaScript, TypeScript, PHP, C#, C++, Java, atau SQL (ANSI). Minimal satu bahasa harus dicentang. Pilih mode **Acak merata** (bahasa dipilih per soal dalam putaran acak) atau **Acak satu bahasa per sesi** (semua soal satu sesi memakai satu bahasa; sesi berikutnya mendapat giliran berikutnya). Semua bahasa tercentang mendapat satu giliran sebelum putaran diacak ulang.
3. Pilih model, tingkat kesulitan, dan kategori, lalu simpan. Default koneksi `https://api.kelontongai.id/v1`, model `deepseek-v4.1-flash`. Katalog bawaan dapat dicari, model dapat diketik manual, atau disegarkan lewat `/models`.
4. Klik **Mulai latihan sekarang**. Pilih salah satu dari empat opsi, periksa jawaban, lalu baca pembahasan. Kode dapat diseleksi dan disalin; indentasi dan baris panjang dipertahankan.
5. Untuk popup otomatis, aktifkan Windows Task Scheduler di Settings. Default: **3 soal setiap 30 menit, kesulitan campuran, timer dan jadwal nonaktif**.

Kategori bahasa pemrograman: dasar bahasa, output kode, debugging, algoritma, struktur data, kompleksitas, dan OOP. SQL memiliki kategori SQL dengan prompt yang meminta skema/data contoh bila diperlukan. Kategori dirotasi secara terpisah untuk setiap bahasa. Kategori yang tersedia mengikuti gabungan bahasa tercentang; pilihan yang masih tersedia dipertahankan dan kategori baru dicentang otomatis. Setiap bahasa tercentang memerlukan minimal satu kategori yang sesuai.

Sisa putaran bahasa disimpan secara terpisah untuk kedua mode dan berlanjut setelah aplikasi ditutup. Mengganti mode melanjutkan putarannya sendiri. Mengubah kumpulan bahasa memulai ulang kedua putaran; perubahan urutan checkbox, jumlah soal, atau timer tidak mereset putaran. Giliran dan kategori baru dimajukan setelah semua soal berhasil disiapkan. Kegagalan atau pembatalan API tidak menghabiskan giliran. Bahasa yang sama dapat muncul berurutan pada batas dua putaran.

Pengaturan lama versi 1 dibaca sebagai satu bahasa tercentang sesuai pilihan sebelumnya. Pengaturan baru memakai versi 2 dengan daftar `selectedLanguages` dan `languageMode` (`BalancedQuestions` atau `SingleLanguagePerSession`). Default: Python dan Acak merata. Riwayat serta soal tersimpan tetap dapat dibaca.

Kode hanya ditampilkan, tidak dikompilasi atau dijalankan. Struktur soal divalidasi, tetapi kebenaran materi dan pembahasan AI tetap dapat keliru. Pembahasan langsung tersedia setelah jawaban dikirim. Riwayat menyimpan 100 sesi terakhir beserta kode, jawaban pengguna, kunci, dan pembahasan.

Jika generasi API gagal dan cache cukup, tombol latihan ulang tersedia. Cache harus mencukupi seluruh pasangan bahasa-kategori dalam rencana sesi yang sama, tanpa soal duplikat, dan mengikuti kesulitan aktif (Campuran menerima semua tingkat). Jika salah satu pasangan tidak cukup, latihan cache tidak ditawarkan. Memeriksa cache tidak mengundi ulang rencana; menggunakan cache memajukan giliran sekali setelah soal lengkap. Sesi ini ditandai sebagai ulangan dan tidak meminta soal baru ke API.

## Jadwal dan data

Popup terjadwal tetap dapat berjalan ketika panel ditutup. Komputer tidak dibangunkan; jadwal saat terkunci/tidur dilewati. Jika kuis masih terbuka, popup berikutnya dilewati. Timer dijeda ketika komputer terkunci/tidur. Jendela selalu dapat ditutup.

Data disimpan di `%LOCALAPPDATA%\CodingFlashcards`: `settings.json`, `key.protected`, `practice.db`, serta cache `models.json`. Identitas task adalah `CodingFlashcards-<SID pengguna>`. Data, mutex, dan task terpisah dari QuizFlashcards. Tidak ada impor riwayat atau key tersimpan dari aplikasi asal; environment key dapat dipakai bersama.

Pertahankan lokasi executable setelah mengaktifkan jadwal. Jika memindahkan aplikasi, nonaktifkan jadwal di lokasi lama lalu aktifkan dari lokasi baru. Tidak memerlukan administrator. Key tidak ditulis ke source/log; tidak ada penggantian model otomatis.

## Build dan instalasi

```powershell
python scripts/bootstrap.py
.\scripts\build.ps1
.\scripts\build.ps1 -Publish
```

SDK .NET 10 dan paket runtime diisolasi di `.tools`. Pada workspace ini toolchain disalin dari proyek referensi yang sudah tersedia; bootstrap tetap dapat menyiapkan toolchain secara mandiri. Build publish menghasilkan paket Windows x64 beserta runtime di `artifacts/publish`.

Instalasi per pengguna (opsional, jalankan sendiri):

```powershell
.\scripts\install.ps1 -Launch
```

Pastikan lokasi publish standar sudah diperbarui dengan build di atas sebelum menjalankan installer; installer membaca folder `artifacts/publish`.

Target instalasi `%LOCALAPPDATA%\Programs\CodingFlashcards` dengan shortcut Desktop. Tutup aplikasi sebelum memperbarui instalasi, lalu aktifkan jadwal dari lokasi instalasi. Instalasi di luar workspace tidak dijalankan otomatis.

## Pengujian

```powershell
$env:DOTNET_ROOT = "$PWD\.tools\dotnet"
$env:DOTNET_CLI_HOME = "$PWD\.tools\cli"
.\.tools\dotnet\dotnet.exe run --project tests/CodingFlashcards.Checks.csproj -c Release
```

Pengujian memakai direktori unik `artifacts/checks-*` dan key dummy; `CODINGFLASHCARDS_DATA_DIR` adalah override direktori untuk pengujian. Render WPF disimpan pada `artifacts/screens` untuk Beranda, Settings (termasuk pilihan bahasa), Riwayat, dan kuis pada skala 100%, 125%, dan 150%.

Untuk klien HTTP asli dengan Postman simulation lokal, jalankan di terminal terpisah:

```powershell
postman simulation run postman/simulations/quiz.sim.yaml --port 4017 --no-history --output ndjson
```

Kemudian jalankan pengujian di atas dengan tambahan `-- http://127.0.0.1:4017`.

Teruji: executable publish membuka jendela yang responsif dan ditutup normal (exit 0); build/publish, 126 pemeriksaan termasuk migrasi versi 1, checkbox/mode, dua mode dan kelanjutan putaran setelah restart, penggantian mode, cache lengkap/tidak lengkap, pembatalan HTTP dengan rencana yang dapat dipakai ulang, batch campuran 13 soal, delapan bahasa, validasi format dan kunci, filter cache, rotasi, persistensi, skor/timeout, identitas/XML jadwal, alur jawaban dan penyelesaian kuis, serta render WPF. Klien HTTP diuji terhadap fixture lokal untuk generasi normal, respons salah bahasa/kategori/kesulitan, kode wajib kosong, opsi rusak, kunci hilang, duplikat, jumlah salah, JSON rusak, JSON berpagar markdown, HTTP 401/404/429/500, dan timeout. Log request simulation mengonfirmasi jalur HTTP tersebut dijalankan dan percobaan perbaikan dibatasi dua kali.

Hasil simulation membuktikan perilaku klien terhadap fixture buatan lokal; tidak membuktikan akurasi atau kepatuhan model provider. Generasi live serta popup nyata dari Windows Task Scheduler belum diuji pada proyek ini. Status Task Scheduler pada lingkungan pengujian tidak dapat dibaca; identitas dan konfigurasi XML diperiksa tanpa mendaftarkan task pengguna.
