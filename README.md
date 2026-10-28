# WinCalendar

Утилита для Windows 10, которая возвращает события в календарь, открывающийся
кликом по часам в панели задач. После того как Microsoft заменила приложения
«Почта и Календарь» на новый Outlook, агенда в этом флайауте опустела: новый
Outlook не пишет события в системное хранилище, из которого их читает оболочка.
WinCalendar берёт события из ваших источников — ссылки/файлы **iCal** и
календари **Thunderbird** — и записывает их прямо в это хранилище.

```
 iCal (URL/файл) ─┐
                  ├─> WinCalendar: парсер ICS + RRULE,
 Thunderbird      │   чтение календарей Thunderbird
 (storage/кэш)   ─┘                   │
                                      v
                        системный WinRT AppointmentStore
                        (календарь на источник, дифф по state.json)
                                      │
                                      v
                      агенда во флайауте часов (ShellExperienceHost)
```

## Возможности

- Несколько подписок iCal: URL (включая `webcal://`, Basic-авторизация) и
  локальные файлы; у каждой — своё имя и цвет в агенде
- Чтение календарей Thunderbird: локальные (storage), ICS по ссылке/файлу
  из реестра TB, сетевые с офлайн-кэшем
- Повторы: RRULE (DAILY/WEEKLY/MONTHLY/YEARLY, BYDAY с порядковыми,
  BYMONTHDAY, BYMONTH, COUNT, UNTIL), EXDATE/RDATE, переопределения
  RECURRENCE-ID, отмена отдельных экземпляров
- All-day и многодневные события, часовые пояса (IANA → Windows, ~90 зон)
- Трей-значок с сегодняшним числом (обновляется в полночь): **ЛКМ —
  синхронизировать**, ПКМ — меню (синхронизация, настройки, лог, автозапуск);
  редактор настроек с GUI
- Напоминания (тосты) настраиваются per-подписку
- Сборка без SDK — системным компилятором C# (.NET Framework 4.8, уже в Windows)

## Установка

Вариант 1 — **MSI** (per-user, без прав администратора):

```cmd
msi.cmd        :: соберёт bin\WinCalendar.msi (~2.4 МБ, WiX 3.11 скачается сам)
bin\WinCalendar.msi
```

Устанавливает в `%LOCALAPPDATA%\WinCalendar`, создаёт ярлык в меню Пуск и
включает автозапуск. Удаление — через «Приложения и возможности» (WinCalendar).

Вариант 2 — скрипт-установщик:

```cmd
install.cmd
```

Соберёт (если нужно), скопирует в `%LOCALAPPDATA%\WinCalendar`, создаст ярлык
в меню Пуск и включит автозапуск. Удаление — `uninstall.cmd`
(конфиг в `%APPDATA%\WinCalendar` не трогается).

Вариант 3 — портативно: собрать (`build.cmd`) и запустить `bin\wincal.exe`
откуда угодно.

После запуска появится значок в трее. Создаётся конфиг по умолчанию с примером
(`%APPDATA%\WinCalendar\config.json` + `sample.ics` — два тестовых события).
Открыть настройки: правый клик по значку → «Настройки…». Командный режим:

```cmd
wincal.exe sync-now              :: синхронизировать и выйти
wincal.exe list                  :: перечислить наши календари (в лог)
wincal.exe delete-calendar ИМЯ   :: удалить календарь из хранилища
```

Лог: `%APPDATA%\WinCalendar\log.txt`.

## Настройка (config.json)

```jsonc
{
  "syncIntervalMinutes": 15,     // минимум 5
  "daysForward": 180,            // горизонт синхронизации вперёд
  "daysBackward": 7,             // и назад (недавние прошедшие тоже видны)
  "prefix": "",                  // префикс имён календарей
  "autostart": true,
  "sources": [
    {
      "name": "Работа",
      "url": "https://example.com/calendar.ics",  // webcal:// тоже можно
      "user": "", "password": "",                 // Basic-авторизация
      "color": "#0078D7",
      "enabled": true,
      "reminderMinutes": -1                       // >=0 — тост за N минут
    },
    { "name": "Дом", "path": "C:\\cal\\home.ics", "color": "#107C10", "enabled": true }
  ],
  "thunderbird": {
    "enabled": true,
    "profile": "",        // пусто = автоопределение из profiles.ini
    "calendars": null     // null = все; либо список имён
  }
}
```

Конфиг правится на ходу — изменение подхватывается при ближайшей синхронизации.

## Thunderbird

Читаются календари типа **storage** и **iCalendar (ICS)** — напрямую из
реестра календарей (`prefs.js`) и базы `calendar-data/local.sqlite`
(копируется во временный файл, работающий Thunderbird не мешает).
Для сетевых (CalDAV) календарей локальной копии нет, пока в Thunderbird не
включён офлайн-кэш (галка «Кэш» в свойствах календаря) — после включения
события появятся в кэше и утилита их подхватит. Прямой CalDAV-доступ возможен
только для серверов с Basic-авторизацией; Google требует OAuth.

## Сборка из исходников

Нужна только Windows 10 с .NET Framework 4.8 (уже в системе). Никаких SDK.

```cmd
build.cmd
```

Скрипт сам скачает SQLite-библиотеки (`get-libs.cmd`, NuGet, public domain)
и соберёт `bin\wincal.exe` через системный `csc.exe`. Для диагностик есть
`bin\smoke.exe` (`diag`, `scan`, `tb`, `parse`, `expand`, `get`, `delcal`).

## Известные ограничения

- Win10 19045 — целевая система; в Windows 11 агенда во флайауте работает
  иначе (Microsoft вернула её там через новый Outlook).
- События синхронизируются в окне `[сегодня - daysBackward; + daysForward]`.
- События, добавленные кнопкой во флайауте, удалить/изменить нельзя — только
  добавить (ограничение флайаута). События наших календарей управляются
  через конфиг/источники.
- Чтение Thunderbird — только то, что TB хранит локально (см. выше).

## Как это устроено (результат реверса)

**Почему агенда опустела.** Флайаут часов рисует `ShellExperienceHost.exe`
(UI-часть — `ClockFlyoutExperience.dll`, классы `ClockFlyout.Models.ICalendarProvider`).
Данные берутся из системного WinRT-хранилища
`Windows.ApplicationModel.Appointments`, куда их складывало приложение
«Календарь» (`microsoft.windowscommunicationsapps`) через платформу Comms
(`%LOCALAPPDATA%\Comms\UnistoreDB`). 31.12.2024 Microsoft отключила
синхронизацию этих приложений, а новый Outlook в хранилище не пишет.
В Windows 11 агенду вернули (через новый Outlook), для Windows 10 — нет.

**Как пишет WinCalendar.** WinRT API
`AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AppCalendarsReadWrite)`
доступен любому классическому приложению — package identity не нужен.
Созданный календарь и его события немедленно видны в агенде. Сборка — прямым
подключением winmd из `C:\Windows\System32\WinMetadata`; await-обёртка своя
(`src/Rt.cs`), потому что `System.Runtime.WindowsRuntime.dll` требует
агрегированного `Windows.winmd` из Windows SDK.

**Тонкость синхронизации.** После выхода процесса его записи перестают
возвращаться перечислением (`FindAppointmentCalendarsAsync`,
`FindAppointmentsAsync` пусты даже с `IncludeHidden`), но остаются видимыми
оболочке и доступны точечно по `LocalId` (`GetAppointmentAsync` /
`GetAppointmentCalendarAsync`). Поэтому утилита ведёт в `state.json` карту
«ключ события → localId|хеш» и синхронизирует точечными запросами: создаёт,
удаляет и обновляет только нужное. Ключ экземпляра стабилен: `uid#ticks`.

## Структура

```
build.cmd            — сборка (csc + winmd + SQLite)
get-libs.cmd         — загрузка System.Data.SQLite с NuGet
msi.cmd              — сборка bin\WinCalendar.msi (WiX 3.11, скачивается сам)
installer/WinCalendar.wxs — сценарий MSI (per-user, ярлык, автозапуск)
install.cmd          — установка (папка, ярлык, автозапуск)
uninstall.cmd        — удаление
src/Rt.cs            — WinRT await-хелперы
src/Config.cs        — конфиг/состояние/лог
src/Ics.cs           — парсер iCal + RRULE + карта часовых поясов
src/StoreSync.cs     — синхронизация в AppointmentStore
src/Thunderbird.cs   — чтение календарей Thunderbird
src/SettingsForm.cs  — GUI настроек
src/Program.cs       — трей-приложение и командный режим
```

## Лицензия

MIT — см. [LICENSE](LICENSE).
SQLite-библиотеки (System.Data.SQLite) — public domain.
