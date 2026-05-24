# Password Break

## Opis projektu
Rozproszony system do łamania hashy SHA-256 metodą brute-force lub dictionary z użyciem gRPC.

## Architektura systemu
- password-break-server
- password-break-client
- password-break-monitor

## Technologie
- .NET 10
- gRPC

## Sposób działania
1. Serwer dzieli zadania
2. Klient pobiera taski
3. Klient liczy hashe
4. Wyniki wracają do serwera
5. Monitor pokazuje status

## Uruchomienie

### Start servera
```bash
cd password-break-server
dotnet run
```

### Start klienta

```bash
cd password-break-client
dotnet run http://localhost:5210
```

### Start monitora

```bash
cd password-break-monitor
dotnet run http://localhost:5210
```

## Testy

```bash
dotnet test
```
