# Dynamic Config V2

## Introduction

Dynamic Config is a name given to a service that allows us to change the way our applications work in real-time, without redeployments or new builds.  Initially this was done with a Groovy service; we had one for nonprod environments and one for prod environments.  Values were dependent on the game key, which is specific to each `RUMBLE_DEPLOYMENT`.  Config values could not be deleted through the publishing app and there was no organization beyond alphabetical order, and at least three separate web pages were used to manage values consumed by applications.  Values were stored in Redis.

V2 removes Redis, moves to C# with the rest of Platform, and seeks to make maintenance easier by separating things into sections.  Since Portal is now specific to each environment, there's no need to select an environment when managing values, and comments can be added to values for internal documentation.

## Overview

Dynamic Config is broken up into two parts: a Platform service running in our cluster and a singleton class in platform-common.

### In The Cluster

As with all other Platform services, dynamic config is deployed in its own container.  It has its own Mongo database and is currently limited to one running instance per environment.  All of the config values are stored in **Sections**.  Sections include:

| Name      | Description                                                                                       |
|:----------|:--------------------------------------------------------------------------------------------------|
| Global    | A catch-all section.  It's discouraged to use this, but available for anything you need to store. |
| Common    | Platform-common-specific values.  No other service should be using these values.                  |
| {Project} | Each Platform project gets its own section: e.g. Leaderboards, Chat                               |

### In Platform-Common

As an extension of `PlatformTimerService`, the dynamic config V2 singleton is created with every service by default.  Currently named `DC2Service` to avoid any confusion with the V1 singleton, it is responsible to regularly fetching new variables from the cluster.

During startup, as long as it's enabled, a Platform service will **register** itself with the cluster.  This provides the cluster with all the information it needs about what's connected to it.  This includes endpoint information, HTTP methods, guids, platform-common version numbers, and service version numbers.  While not currently utilized by Portal, the intent is for this information to be used to simplify diagnostics and deployments.  Registration also creates the Section and, if this happens, the cluster generates a new admin token for it.

Every `PlatformController` is created with a reference to `DC2Service`, `_dc2Service`.  Being a singleton, it can also be inserted via dependency injection into any class that supports it.

Furthermore, every section contains an admin token.  It is standard for every service to have its own admin token and this eases management of those tokens.

## Usage

Usage within Platform projects is designed to be very simple and straightforward.  However, being a new implementation, there are some potential pitfalls of using `DC2Service` incorrectly.  Access is very open right now while testing it out.  In the future, the singleton will be hardened.

### Design Intent

```
string myVar = _dc2Service.Optional<string>("myVar"); // Require<T>() also works; will throw an exception if it's not found.
string adminToken = _dc2Service.AdminToken;
```

That's it.  No bells, no whistles.  Two methods should be sufficient for all use cases.  However, it's important to note what's happening when you call `Optional<T>()`:

* First, the current project's **Section** is scanned for the key you asked for.
* If nothing is found, the Common section is used.
* If nothing is found, the Global section is used.
* If nothing is found, **all** sections are scanned.

Consequently there is a slight performance consideration if you're trying to access a section that does not belong to the context of your application.  There shouldn't be any need to do this, however.  It's bad practice for one application to be linked to another's config.

## Future Enhancements

* Add a `Require<T>()` to DC2Service.
* Rename DC2Service to ConfigService; remove existing DynamicConfigService