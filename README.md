# Broadcast + TCP та синхронізація папок (C# .NET 8)

Рішення містить 4 консольні застосунки:
- **BroadcastServer** — генерує випадкові повідомлення, зберігає текст за ID, розсилає `IP:TCP-порт:ID` через UDP Broadcast.
- **BroadcastClient** — слухає UDP Broadcast, підключається до сервера через TCP і отримує текст повідомлення за ID.
- **SyncServer** — стежить за папкою `Shared` через `FileSystemWatcher`, транслює зміни через UDP Broadcast і передає файли по TCP.
- **SyncClient** — слухає broadcast та створює/оновлює/видаляє файли в папці `Synced`.

## Вимоги
- Windows і .NET 8 SDK
- Visual Studio 2022 або `dotnet` CLI
- Дозвіл Windows Firewall для локальних TCP/UDP-з'єднань, якщо з'явиться відповідний запит.

## Відкрити
Відкрийте `BroadcastAndSync.sln` у Visual Studio 2022 і виконайте Build Solution.

Або з папки проєкту:
```powershell
dotnet restore BroadcastAndSync.sln
dotnet build BroadcastAndSync.sln
```

## Завдання 1: Broadcast + TCP
У двох окремих терміналах запустіть:
```powershell
dotnet run --project BroadcastServer
dotnet run --project BroadcastClient
```
Сервер використовує TCP `7000` і UDP Broadcast `7001`. Нові повідомлення створюються приблизно кожні 2–7 секунд.

## Завдання 2: Sync
У двох окремих терміналах запустіть:
```powershell
dotnet run --project SyncServer
dotnet run --project SyncClient
```
Серверна папка `Shared` і клієнтська `Synced` створюються автоматично всередині каталогів запуску відповідних застосунків. Додавайте, змінюйте та видаляйте файли в `Shared`. Клієнт відобразить зміни в `Synced`.

## Примітки
- UDP Broadcast працює в межах локального сегмента мережі; для різних Wi-Fi/VLAN маршрутизатор може блокувати broadcast.
- Для простоти підтримуються файли у корені папок, без підпапок.
- Файловий протокол передає ім'я файлу рядком, а вміст — двійковими байтами з довжиною в заголовку.
- Це навчальний приклад для локальної мережі, не захищений протокол для недовірених мереж.
