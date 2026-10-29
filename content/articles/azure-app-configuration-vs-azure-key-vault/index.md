---
title: "Azure App Configuration vs. Azure Key Vault"
date: 2025-09-26
slug: azure-app-configuration-vs-azure-key-vault
description: "A detailed comparison of Azure App Configuration and Azure Key Vault, since they serve different but complementary roles in cloud-native applications."
tags: [azure, security]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

**Rule of thumb**:

* **Secrets → Key Vault**
* **Configs → App Configuration**
* **Use both for secure + flexible configuration management.**

## Azure Key Vault

**Purpose**: Securely store **sensitive information** such as secrets, keys, and certificates.

* **Use Cases**:

  * Store API keys, connection strings, tokens.
  * Manage and rotate TLS/SSL certificates.
  * Protect cryptographic keys used for encryption/decryption.
* **Strengths**:

  * Built-in hardware security module (HSM) support.
  * Access policies and RBAC for fine-grained control.
  * Automatic secret rotation with some Azure services.
  * Logging and monitoring via Azure Monitor.
* **Limitations**:

  * Not designed for feature flags or configuration settings that change frequently.
  * API calls can add latency if used excessively at runtime.

## Azure App Configuration

**Purpose**: Centralized **application configuration management**.

* **Use Cases**:

  * Store non-sensitive app settings (feature flags, UI options, app behavior).
  * Versioned configurations and labels (per environment, per region).
  * Enable **dynamic configuration refresh** in apps.
* **Strengths**:

  * Feature flag management built-in.
  * Supports key-value pairs with labels for environment separation.
  * Integration with Azure Functions, App Service, AKS, and more.
  * High availability and global distribution.
* **Limitations**:

  * Not designed to store secrets or keys.
  * Does not provide encryption key lifecycle management.

## When to Use Which

* **Use Key Vault when**:

  * Handling **secrets** (DB passwords, API keys).
  * Managing certificates and encryption keys.
  * Need secure storage with strong access policies.

* **Use App Configuration when**:

  * Handling **app configs** (feature flags, toggle dark mode, regional endpoints).
  * Need **dynamic refresh** without redeployment.
  * Want **environment-based configuration** with versioning.

## How They Work Together

In most real-world solutions, you **combine both**:

* Use **Azure App Configuration** for *general settings* and feature management.
* Reference **Azure Key Vault** inside App Configuration for sensitive values.
* Example:

  * `AppConfig:DbConnectionString` → points to Key Vault secret.
  * App reads all configs from App Configuration → securely resolves secrets from Key Vault when needed.
