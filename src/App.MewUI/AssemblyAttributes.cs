using System.Runtime.Versioning;

// 本アプリは Windows 専用である（MewUI / AWT のウィンドウ API と Win32 のクリップボードを使う）。
// 対象フレームワークは net11.0 のまま（隣の UI と同じ実行ファイルの作りを保つ）ため、
// Windows 10 1809 以降を要求することをアセンブリ全体で宣言する。
[assembly: SupportedOSPlatform("windows10.0.17763")]
