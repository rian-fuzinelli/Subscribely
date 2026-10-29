#!/usr/bin/env python3
"""Falha a CI quando a cobertura de linhas fica abaixo dos limites da spec 09.

Uso: python3 scripts/check-coverage.py TestResults/coverage/report/Cobertura.xml
O arquivo é o Cobertura consolidado pelo ReportGenerator (unit + integration).
"""
import sys
import xml.etree.ElementTree as ET

# Assembly → cobertura mínima de linhas (%).
ASSEMBLIES = {
    "Subscribely.Modules.Billing": 90.0,
    "Subscribely.Modules.Payments": 90.0,
}

# Classe (nome completo) → cobertura mínima de linhas (%).
CLASSES = {
    "Subscribely.Modules.Billing.Domain.Invoices.ProrationCalculator": 100.0,
    "Subscribely.Modules.Billing.Domain.Subscriptions.Subscription": 100.0,
    "Subscribely.Modules.Payments.Features.Webhooks.WebhookSignature": 100.0,
}


def rate(element):
    return float(element.get("line-rate", "0")) * 100


def main(path):
    root = ET.parse(path).getroot()
    packages = {p.get("name"): p for p in root.iter("package")}
    classes = {c.get("name"): c for c in root.iter("class")}
    failures = []

    for name, minimum in {**ASSEMBLIES, **CLASSES}.items():
        element = packages.get(name) if name in ASSEMBLIES else classes.get(name)
        if element is None:
            failures.append(f"{name}: not found in the report")
            continue
        actual = rate(element)
        status = "ok" if actual + 1e-9 >= minimum else "FAIL"
        print(f"{status:4} {name}: {actual:.1f}% (minimum {minimum:.0f}%)")
        if status == "FAIL":
            failures.append(f"{name}: {actual:.1f}% < {minimum:.0f}%")

    print(f"total: {rate(root):.1f}%")
    if failures:
        print("\nCoverage thresholds not met:\n  " + "\n  ".join(failures))
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else "TestResults/coverage/report/Cobertura.xml"))
