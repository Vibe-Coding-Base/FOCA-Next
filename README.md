
<p align="left">
<img src="./doc/FOCA_White.jpg"/>
</p>

# FOCA (Fingerprinting Organizations with Collected Archives)

**FOCA  is a tool used mainly to find metadata and hidden information in the documents it scans**. These documents may be on web pages, and can be downloaded and analysed with FOCA.

It is capable of analysing a wide variety of documents, with the most common being **Microsoft Office**, **Open Office**, or **PDF** files, although it also analyses Adobe InDesign or SVG files, for instance.

These documents are searched for using three possible search engines: **Google**, **Bing**, and **DuckDuckGo**. The sum of the results from the three engines amounts to a lot of documents. It is also possible to add local files to extract the EXIF information from graphic files, and a complete analysis of the information discovered through the URL is conducted even before downloading the file.

## 🏭 Releases 

Check [here](https://github.com/ElevenPaths/FOCA/releases) our latest releases. 

## 🖥️ Cross-platform FOCA (Windows, Linux, macOS)

The release packages contain two self-contained programs; nothing else needs to be installed:

* **`FOCA`**: the desktop application, with the familiar layout (project tree, documents, metadata summary, log). *View > Theme* (or the button at the top right) switches between light, dark and the system setting.
* **`foca-cli`**: the same engine from the command line, for scripts and servers.

Projects are stored in a local SQLite database: `%LOCALAPPDATA%\FOCA` on Windows, `~/.local/share/FOCA` on Linux and `~/Library/Application Support/FOCA` on macOS. SQL Server is optional (`--sqlserver "<connection string>"`).

### Document analysis without a domain

A project does not need a domain. Choose **Document analysis** when creating it and point FOCA at a folder of documents; a domain can be added later to enable web search.

```sh
foca-cli analyze ./documents -r                          # report on the console
foca-cli analyze ./documents -r --project "Audit 2026"   # also save the results
foca-cli export "Audit 2026" -f html -o report.html      # HTML, JSON or CSV
```

### Documents published by a domain

```sh
foca-cli project create "ACME" --domain acme.com
foca-cli search "ACME" --wayback --download              # search engines + Internet Archive, then analyse
foca-cli project show "ACME"
```

Projects of the previous FOCA can be imported with `foca-cli import-legacy -c "<SQL Server connection string>"`.

### Plugins

Plugins (API v2) go in their own folder under the FOCA data folder (`plugins/<name>`, with a `plugin.json`). They run with the same rights as FOCA, so each plugin must be approved in *Plugins > Load / approve plugins*; a modified plugin needs approval again. See `Plugin example/HelloPlugin`.

### Building

```sh
./build.ps1          # builds and tests everything (the WinForms version too, on Windows)
./publish.ps1 -Runtime linux-x64 -Version 4.0.0
```

Requires the .NET 10 SDK; the WinForms version additionally needs Visual Studio or Build Tools on Windows.

## ✔️ Requisites (legacy WinForms version)

To run the original Windows version (`FOCA3_Pro.sln`) the system will need:

* **Microsoft Windows** (64 bits).
* **Microsoft .NET Framework 4.8**.
* Microsoft **Visual C++ 2010 x64** or greater.
* An instance of **SQL Server** 2014 or greater, or SQL Server LocalDB.

#### 📝Notes

* When starting, the app connects to `.\SQLEXPRESS`, then the default instance and LocalDB. If none is usable, it shows the error and prompts for a connection string.

## 📜 Licence

[GNU GENERAL PUBLIC LICENCE](https://www.gnu.org/licenses/gpl-3.0.en.html)

## 🔊 Stay tuned

Get the news about our latest doings and send us a message. We 💛 hearing from you. 

* [<img src="./doc/Twitter_Social_Icon_Circle_Color.svg" width="30" height="24"/>](https://twitter.com/Fear_the_Foca)  https://twitter.com/Fear_the_Foca


## ☕ Further reading 

* https://www.elevenpaths.com/labstools/foca/index.html
* [ES] https://0xword.com/es/libros/59-pentesting-con-foca.html
* [ES] https://es.wikipedia.org/wiki/FOCA_Tool
* [ES] https://empresas.blogthinkbig.com/como-analizar-documentos-con-foca/
