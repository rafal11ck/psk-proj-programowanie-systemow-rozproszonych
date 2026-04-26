import pandas as pd
import matplotlib.pyplot as plt
from pathlib import Path

METRICS_FILE = Path("metrics.csv")
OUT_DIR = Path("charts")

if not METRICS_FILE.exists():
    print(f"Nie znaleziono pliku: {METRICS_FILE}")
    print("Uruchom skrypt z folderu eksperymentu, w którym jest metrics.csv.")
    print("Przykład:")
    print(r"cd .\experiments\nazwa_eksperymentu")
    print("py ..\\..\\plot_metrics.py")
    exit(1)

df = pd.read_csv(METRICS_FILE)

if df.empty:
    print("Plik metrics.csv jest pusty.")
    exit(1)

OUT_DIR.mkdir(parents=True, exist_ok=True)

# Numer taska, żeby wygodnie rysować oś X
df["task_no"] = range(1, len(df) + 1)

# Konwersja timestampów z metrics.csv
for col in ["run_started_at_utc", "task_sent_at_utc", "result_received_at_utc"]:
    if col in df.columns:
        df[col] = pd.to_datetime(df[col], errors="coerce")

# Suma znalezionych haseł narastająco
df["cumulative_found"] = df["found_count"].cumsum()

# Parametry eksperymentu
experiment_name = str(df["experiment_name"].iloc[0]) if "experiment_name" in df.columns else "experiment"
attack_mode = str(df["attack_mode"].iloc[0])

charset_length = int(df["charset_length"].iloc[0])
min_length = int(df["min_length"].iloc[0])
max_length = int(df["max_length"].iloc[0])
target_hashes_count = int(df["target_hashes_count"].iloc[0])
chunk_size = int(df["chunk_size"].iloc[0])
clients_count = int(df["clients_count"].iloc[0])
client_threads = int(df["client_threads"].iloc[0])

# Liczba wszystkich kandydatów dla brute force
total_candidates = sum(charset_length ** length for length in range(min_length, max_length + 1))

# Procent przeszukanej przestrzeni
df["progress_percent"] = ((df["end_index"] + 1) / total_candidates) * 100

# Dodatkowe metryki
df["compute_percent"] = (df["compute_time_ms"] / df["total_time_ms"]) * 100
df["communication_percent"] = (df["communication_time_ms"] / df["total_time_ms"]) * 100

# Podstawowe podsumowania
found_total = int(df["found_count"].sum())
searched_candidates = int(df["candidates_count"].sum())

avg_throughput = df["throughput_candidates_per_sec"].mean()
median_throughput = df["throughput_candidates_per_sec"].median()

avg_compute = df["compute_time_ms"].mean()
avg_communication = df["communication_time_ms"].mean()
avg_total = df["total_time_ms"].mean()
max_total = df["total_time_ms"].max()

# Realny czas eksperymentu: od pierwszego wysłania taska do ostatniego wyniku
if "task_sent_at_utc" in df.columns and "result_received_at_utc" in df.columns:
    real_start = df["task_sent_at_utc"].min()
    real_end = df["result_received_at_utc"].max()

    if pd.notna(real_start) and pd.notna(real_end):
        real_duration_sec = (real_end - real_start).total_seconds()
    else:
        real_duration_sec = df["total_time_ms"].sum() / 1000
else:
    real_duration_sec = df["total_time_ms"].sum() / 1000

overall_throughput = searched_candidates / real_duration_sec if real_duration_sec > 0 else 0


def save_plot(filename):
    plt.tight_layout()
    plt.savefig(OUT_DIR / filename, dpi=150)
    plt.close()


# 1. Przepustowość per task
plt.figure(figsize=(11, 5))
plt.plot(df["task_no"], df["throughput_candidates_per_sec"])
plt.xlabel("Numer taska")
plt.ylabel("Kandydaci na sekundę")
plt.title("Przepustowość brute force per task")
plt.grid(True)
save_plot("01_throughput_per_task.png")


# 2. Przepustowość ze średnią
plt.figure(figsize=(11, 5))
plt.plot(df["task_no"], df["throughput_candidates_per_sec"], label="Przepustowość per task")
plt.axhline(
    avg_throughput,
    linestyle="--",
    label=f"Średnia per task: {avg_throughput:,.0f}".replace(",", " ")
)
plt.axhline(
    overall_throughput,
    linestyle=":",
    label=f"Realna ogólna: {overall_throughput:,.0f}".replace(",", " ")
)
plt.xlabel("Numer taska")
plt.ylabel("Kandydaci na sekundę")
plt.title("Przepustowość brute force ze średnimi")
plt.legend()
plt.grid(True)
save_plot("02_throughput_average.png")


# 3. Czas obliczeń i komunikacji
plt.figure(figsize=(11, 5))
plt.plot(df["task_no"], df["compute_time_ms"], label="Czas obliczeń")
plt.plot(df["task_no"], df["communication_time_ms"], label="Czas komunikacji")
plt.xlabel("Numer taska")
plt.ylabel("Czas [ms]")
plt.title("Czas obliczeń vs czas komunikacji")
plt.legend()
plt.grid(True)
save_plot("03_compute_vs_communication.png")


# 4. Całkowity czas taska
plt.figure(figsize=(11, 5))
plt.plot(df["task_no"], df["total_time_ms"])
plt.xlabel("Numer taska")
plt.ylabel("Czas [ms]")
plt.title("Całkowity czas obsługi taska")
plt.grid(True)
save_plot("04_total_time.png")


# 5. Znalezione hasła narastająco
plt.figure(figsize=(11, 5))
plt.step(df["task_no"], df["cumulative_found"], where="post")
plt.xlabel("Numer taska")
plt.ylabel("Liczba znalezionych haseł")
plt.title("Znalezione hasła narastająco")
plt.ylim(0, max(target_hashes_count, found_total) + 1)
plt.grid(True)
save_plot("05_found_cumulative.png")


# 6. Postęp przeszukiwania przestrzeni
plt.figure(figsize=(11, 5))
plt.plot(df["task_no"], df["progress_percent"])
plt.xlabel("Numer taska")
plt.ylabel("Postęp [%]")
plt.title("Postęp przeszukiwania przestrzeni haseł")
plt.grid(True)
save_plot("06_progress.png")


# 7. Taski, w których znaleziono hasła
found_tasks = df[df["found_count"] > 0]

plt.figure(figsize=(11, 5))
if found_tasks.empty:
    plt.text(0.5, 0.5, "Nie znaleziono żadnych haseł", ha="center", va="center")
    plt.xlim(0, 1)
    plt.ylim(0, 1)
else:
    plt.bar(found_tasks["task_no"], found_tasks["found_count"])
    plt.xlabel("Numer taska")
    plt.ylabel("Liczba znalezionych haseł")

plt.title("Taski, w których znaleziono hasła")
plt.grid(True)
save_plot("07_found_per_task.png")


# 8. Histogram całkowitego czasu tasków
plt.figure(figsize=(11, 5))
plt.hist(df["total_time_ms"], bins=40)
plt.xlabel("Całkowity czas taska [ms]")
plt.ylabel("Liczba tasków")
plt.title("Rozkład całkowitego czasu obsługi tasków")
plt.grid(True)
save_plot("08_total_time_histogram.png")


# 9. Histogram przepustowości
plt.figure(figsize=(11, 5))
plt.hist(df["throughput_candidates_per_sec"], bins=40)
plt.xlabel("Kandydaci na sekundę")
plt.ylabel("Liczba tasków")
plt.title("Rozkład przepustowości")
plt.grid(True)
save_plot("09_throughput_histogram.png")


# 10. Średni czas obliczeń i komunikacji
plt.figure(figsize=(11, 5))
labels = ["Obliczenia", "Komunikacja"]
values = [avg_compute, avg_communication]
plt.bar(labels, values)
plt.ylabel("Średni czas [ms]")
plt.title("Średni czas obliczeń i komunikacji")
plt.grid(True, axis="y")
save_plot("10_avg_compute_vs_communication_bar.png")


# 11. Przepustowość w czasie
if "result_received_at_utc" in df.columns and df["result_received_at_utc"].notna().any():
    df["elapsed_sec"] = (
        df["result_received_at_utc"] - df["result_received_at_utc"].min()
    ).dt.total_seconds()

    plt.figure(figsize=(11, 5))
    plt.plot(df["elapsed_sec"], df["throughput_candidates_per_sec"])
    plt.xlabel("Czas od startu [s]")
    plt.ylabel("Kandydaci na sekundę")
    plt.title("Przepustowość w czasie")
    plt.grid(True)
    save_plot("11_throughput_over_time.png")


# 12. CSV pod porównanie wielu eksperymentów
summary_row = pd.DataFrame([{
    "experiment_name": experiment_name,
    "attack_mode": attack_mode,
    "chunk_size": chunk_size,
    "clients_count": clients_count,
    "client_threads": client_threads,
    "tasks_count": len(df),
    "target_hashes_count": target_hashes_count,
    "found_passwords": found_total,
    "searched_candidates": searched_candidates,
    "progress_percent": df["progress_percent"].iloc[-1],
    "real_duration_sec": real_duration_sec,
    "avg_throughput_candidates_per_sec": avg_throughput,
    "median_throughput_candidates_per_sec": median_throughput,
    "real_throughput_candidates_per_sec": overall_throughput,
    "avg_compute_time_ms": avg_compute,
    "avg_communication_time_ms": avg_communication,
    "avg_total_time_ms": avg_total,
    "max_total_time_ms": max_total
}])

summary_row.to_csv(OUT_DIR / "experiment_summary.csv", index=False)


# 13. Podsumowanie tekstowe
summary_text = f"""Podsumowanie eksperymentu

Plik danych: {METRICS_FILE}
Nazwa eksperymentu: {experiment_name}
Tryb: {attack_mode}

Chunk size: {chunk_size}
Liczba klientów: {clients_count}
Liczba wątków klienta: {client_threads}

Liczba tasków: {len(df)}

Długość haseł: {min_length}-{max_length}
Długość alfabetu: {charset_length}
Liczba hashy docelowych: {target_hashes_count}

Liczba wszystkich kandydatów w przestrzeni: {total_candidates}
Liczba sprawdzonych kandydatów: {searched_candidates}
Postęp przeszukiwania: {df["progress_percent"].iloc[-1]:.6f}%

Znaleziono haseł: {found_total}
Nie znaleziono haseł: {target_hashes_count - found_total}

Czas eksperymentu: {real_duration_sec:.2f} s

Średnia przepustowość per task: {avg_throughput:.2f} kandydatów/s
Mediana przepustowości per task: {median_throughput:.2f} kandydatów/s
Realna przepustowość całego eksperymentu: {overall_throughput:.2f} kandydatów/s

Średni czas obliczeń: {avg_compute:.2f} ms
Średni czas komunikacji: {avg_communication:.2f} ms
Średni całkowity czas taska: {avg_total:.2f} ms
Maksymalny całkowity czas taska: {max_total:.2f} ms
"""

(OUT_DIR / "summary.txt").write_text(summary_text, encoding="utf-8")


print("Gotowe. Wykresy zapisane w folderze:")
print(OUT_DIR)
print()
print(f"Liczba tasków: {len(df)}")
print(f"Znaleziono haseł: {found_total}/{target_hashes_count}")
print(f"Przeszukano około: {df['progress_percent'].iloc[-1]:.6f}% przestrzeni")
print(f"Średnia przepustowość per task: {avg_throughput:,.2f} kandydatów/s".replace(",", " "))
print(f"Realna przepustowość całego eksperymentu: {overall_throughput:,.2f} kandydatów/s".replace(",", " "))
print(f"Podsumowanie zapisane w: {OUT_DIR / 'summary.txt'}")
print(f"CSV do porównań zapisany w: {OUT_DIR / 'experiment_summary.csv'}")