#!/usr/bin/env python3
"""Run all benchmarks 10 times each and compute medians."""

import subprocess
import statistics
import re
import sys

RUNS = 10

def run_cmd(cmd, cwd="/root/projects/PureRpc"):
    result = subprocess.run(cmd, shell=True, capture_output=True, text=True, cwd=cwd, timeout=300)
    return result.stdout + result.stderr

def parse_serializer(output):
    """Parse serializer benchmark output, return dict of {name: qps}."""
    results = {}
    for line in output.splitlines():
        m = re.match(r'\s*\[(\w+)\]\s+([\d,]+)\s+QPS', line)
        if m:
            results[m.group(1)] = int(m.group(2).replace(',', ''))
    return results

def parse_transport(output):
    """Parse transport benchmark summary, return dict of {name: qps}."""
    results = {}
    in_summary = False
    for line in output.splitlines():
        if 'Transport Benchmark Summary' in line:
            in_summary = True
            continue
        if in_summary and '═══' in line and results:
            break
        if in_summary:
            m = re.match(r'\s*(\S+)\s+([\d,]+)\s+QPS', line)
            if m:
                results[m.group(1)] = int(m.group(2).replace(',', ''))
    return results

def parse_endtoend(output):
    """Parse PureRpc end-to-end benchmark, return {qps, latency}."""
    qps = None
    latency = None
    for line in output.splitlines():
        m = re.match(r'\s*Throughput:\s+([\d,.]+)\s+QPS', line)
        if m:
            qps = float(m.group(1).replace(',', ''))
        m = re.match(r'\s*Avg Latency:\s+([\d,.]+)\s+ms', line)
        if m:
            latency = float(m.group(1).replace(',', ''))
    return {'qps': qps, 'latency': latency}

def parse_grpc(output):
    """Parse gRPC benchmark, return {qps, latency}."""
    qps = None
    latency = None
    for line in output.splitlines():
        m = re.match(r'\s*Throughput:\s+([\d,.]+)\s+QPS', line)
        if m:
            qps = float(m.group(1).replace(',', ''))
        m = re.match(r'\s*Avg Latency:\s+([\d,.]+)\s+ms', line)
        if m:
            latency = float(m.group(1).replace(',', ''))
    return {'qps': qps, 'latency': latency}

def median_dict(dicts_list, keys):
    """Given a list of dicts with same keys, compute median for each key."""
    result = {}
    for key in keys:
        vals = [d[key] for d in dicts_list if d.get(key) is not None]
        if vals:
            result[key] = statistics.median(vals)
    return result

# ── Serializer ──
print("=" * 60)
print("Serializer Benchmark (10 runs)")
print("=" * 60)
ser_results = []
for i in range(RUNS):
    print(f"  Run {i+1}/{RUNS}...", end=" ", flush=True)
    output = run_cmd("dotnet run --project PureRpc.BenchMark.Serializer --no-build 2>/dev/null || dotnet run --project PureRpc.BenchMark.Serializer 2>/dev/null")
    parsed = parse_serializer(output)
    if parsed:
        print(parsed)
        ser_results.append(parsed)
    else:
        print("PARSE FAILED")

if ser_results:
    keys = ser_results[0].keys()
    medians = median_dict(ser_results, keys)
    print(f"\n  MEDIANS:")
    for k in sorted(medians, key=lambda x: medians[x], reverse=True):
        print(f"    {k}: {medians[k]:,.0f} QPS")

# ── Transport ──
print("\n" + "=" * 60)
print("Transport Benchmark (10 runs)")
print("=" * 60)
trans_results = []
for i in range(RUNS):
    print(f"  Run {i+1}/{RUNS}...", end=" ", flush=True)
    output = run_cmd("dotnet run --project PureRpc.BenchMark.Transport 2>/dev/null")
    parsed = parse_transport(output)
    if parsed:
        print(parsed)
        trans_results.append(parsed)
    else:
        print("PARSE FAILED")

if trans_results:
    keys = trans_results[0].keys()
    medians = median_dict(trans_results, keys)
    print(f"\n  MEDIANS:")
    for k in sorted(medians, key=lambda x: medians[x], reverse=True):
        print(f"    {k}: {medians[k]:,.0f} QPS")

# ── PureRpc end-to-end ──
print("\n" + "=" * 60)
print("PureRpc End-to-End Benchmark (10 runs)")
print("=" * 60)
e2e_results = []
for i in range(RUNS):
    print(f"  Run {i+1}/{RUNS}...", end=" ", flush=True)
    output = run_cmd("dotnet run --project PureRpc.BenchMark 2>/dev/null")
    parsed = parse_endtoend(output)
    if parsed.get('qps'):
        print(f"QPS={parsed['qps']:.2f}  Latency={parsed['latency']:.4f}ms")
        e2e_results.append(parsed)
    else:
        print("PARSE FAILED")

if e2e_results:
    med_qps = statistics.median([r['qps'] for r in e2e_results])
    med_lat = statistics.median([r['latency'] for r in e2e_results])
    print(f"\n  MEDIANS: QPS={med_qps:,.2f}  Latency={med_lat:.4f}ms")

# ── gRPC ──
print("\n" + "=" * 60)
print("gRPC Benchmark (10 runs)")
print("=" * 60)
grpc_results = []
for i in range(RUNS):
    print(f"  Run {i+1}/{RUNS}...", end=" ", flush=True)
    output = run_cmd("dotnet run --project PureRpc.BenchMark.Grpc 2>/dev/null")
    parsed = parse_grpc(output)
    if parsed.get('qps'):
        print(f"QPS={parsed['qps']:.2f}  Latency={parsed['latency']:.4f}ms")
        grpc_results.append(parsed)
    else:
        print("PARSE FAILED")

if grpc_results:
    med_qps = statistics.median([r['qps'] for r in grpc_results])
    med_lat = statistics.median([r['latency'] for r in grpc_results])
    print(f"\n  MEDIANS: QPS={med_qps:,.2f}  Latency={med_lat:.4f}ms")

# ── Final Summary ──
print("\n" + "=" * 60)
print("FINAL MEDIAN SUMMARY")
print("=" * 60)

if ser_results:
    keys = ser_results[0].keys()
    medians = median_dict(ser_results, keys)
    print("\nSerializer:")
    for k in sorted(medians, key=lambda x: medians[x], reverse=True):
        print(f"  {k}: {medians[k]:,.0f} QPS")

if trans_results:
    keys = trans_results[0].keys()
    medians = median_dict(trans_results, keys)
    print("\nTransport:")
    for k in sorted(medians, key=lambda x: medians[x], reverse=True):
        print(f"  {k}: {medians[k]:,.0f} QPS")

if e2e_results:
    med_qps = statistics.median([r['qps'] for r in e2e_results])
    med_lat = statistics.median([r['latency'] for r in e2e_results])
    print(f"\nPureRpc E2E: {med_qps:,.0f} QPS  {med_lat:.4f}ms latency")

if grpc_results:
    med_qps = statistics.median([r['qps'] for r in grpc_results])
    med_lat = statistics.median([r['latency'] for r in grpc_results])
    print(f"gRPC:        {med_qps:,.0f} QPS  {med_lat:.4f}ms latency")