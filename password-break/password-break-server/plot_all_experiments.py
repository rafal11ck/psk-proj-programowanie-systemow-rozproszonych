import pandas as pd
import matplotlib.pyplot as plt
from pathlib import Path

EXPERIMENTS_DIR = Path("experiments")
OUT_DIR = EXPERIMENTS_DIR / "comparison_charts"
OUT_DIR.mkdir(exist_ok=True)

summary_files = list(EXPERIMENTS_DIR.glob("*/charts/experiment_summary.csv"))

if not summary_files:
    print("Nie znaleziono plików experiment_summary.csv.")
    print("Najpierw uruchom plot_metrics.py w folderach eksperymentów.")
    exit(1)

rows = []

for file in summary_files:
    df = pd.read_csv(file)
    df["experiment_folder"] = file.parents[1].name
    rows.append(df)

summary = pd.concat(rows, ignore_index=True)

summary.to_csv(OUT_DIR / "all_experiments_summary.csv", index=False)

# Sortowanie dla czytelności
summary = summary.sort_values(
    by=["clients_count", "client_threads", "chunk_size"],
    ascending=True
)

# 1. Realna przepustowość względem chunk size
plt.figure(figsize=(11, 5))
for clients in sorted(summary["clients_count"].unique()):
    part = summary[summary["clients_count"] == clients]
    plt.plot(
        part["chunk_size"],
        part["real_throughput_candidates_per_sec"],
        marker="o",
        label=f"{clients} klient/klientów"
    )

plt.xlabel("Chunk size")
plt.ylabel("Realna przepustowość [kandydaci/s]")
plt.title("Wpływ granulacji zadań na przepustowość")
plt.legend()
plt.grid(True)
plt.tight_layout()
plt.savefig(OUT_DIR / "01_chunk_size_vs_throughput.png", dpi=150)
plt.close()

# 2. Realna przepustowość względem liczby klientów
plt.figure(figsize=(11, 5))
for chunk in sorted(summary["chunk_size"].unique()):
    part = summary[summary["chunk_size"] == chunk]
    plt.plot(
        part["clients_count"],
        part["real_throughput_candidates_per_sec"],
        marker="o",
        label=f"chunk {chunk}"
    )

plt.xlabel("Liczba klientów")
plt.ylabel("Realna przepustowość [kandydaci/s]")
plt.title("Wpływ liczby klientów na przepustowość")
plt.legend()
plt.grid(True)
plt.tight_layout()
plt.savefig(OUT_DIR / "02_clients_vs_throughput.png", dpi=150)
plt.close()

# 3. Średni czas taska względem chunk size
plt.figure(figsize=(11, 5))
for clients in sorted(summary["clients_count"].unique()):
    part = summary[summary["clients_count"] == clients]
    plt.plot(
        part["chunk_size"],
        part["avg_total_time_ms"],
        marker="o",
        label=f"{clients} klient/klientów"
    )

plt.xlabel("Chunk size")
plt.ylabel("Średni czas taska [ms]")
plt.title("Wpływ granulacji na średni czas obsługi taska")
plt.legend()
plt.grid(True)
plt.tight_layout()
plt.savefig(OUT_DIR / "03_chunk_size_vs_avg_task_time.png", dpi=150)
plt.close()

# 4. Komunikacja względem chunk size
plt.figure(figsize=(11, 5))
for clients in sorted(summary["clients_count"].unique()):
    part = summary[summary["clients_count"] == clients]
    plt.plot(
        part["chunk_size"],
        part["avg_communication_time_ms"],
        marker="o",
        label=f"{clients} klient/klientów"
    )

plt.xlabel("Chunk size")
plt.ylabel("Średni czas komunikacji [ms]")
plt.title("Wpływ granulacji na koszt komunikacji")
plt.legend()
plt.grid(True)
plt.tight_layout()
plt.savefig(OUT_DIR / "04_chunk_size_vs_communication.png", dpi=150)
plt.close()

# 5. Tabela porównawcza jako CSV
columns = [
    "experiment_name",
    "experiment_folder",
    "chunk_size",
    "clients_count",
    "client_threads",
    "tasks_count",
    "real_throughput_candidates_per_sec",
    "avg_throughput_candidates_per_sec",
    "avg_compute_time_ms",
    "avg_communication_time_ms",
    "avg_total_time_ms",
    "found_passwords",
    "target_hashes_count",
    "progress_percent"
]

existing_columns = [c for c in columns if c in summary.columns]
summary[existing_columns].to_csv(OUT_DIR / "comparison_table.csv", index=False)

print("Gotowe. Wykresy porównawcze zapisane w:")
print(OUT_DIR)
print()
print("Znalezione eksperymenty:")
print(summary[["experiment_name", "chunk_size", "clients_count", "client_threads", "real_throughput_candidates_per_sec"]])