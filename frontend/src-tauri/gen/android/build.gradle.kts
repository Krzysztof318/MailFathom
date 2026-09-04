buildscript {
    repositories {
        google()
        mavenCentral()
    }
    dependencies {
        classpath("com.android.tools.build:gradle:8.11.0")
        classpath("org.jetbrains.kotlin:kotlin-gradle-plugin:1.9.25")
    }
}

allprojects {
    repositories {
        google()
        mavenCentral()
    }

    // Added to what `tauri android init` wrote. Gradle reads a bare version as a preference and takes the highest one
    // requested across the graph, so without this the artifacts behind the five declarations in `app/build.gradle.kts`
    // move whenever either repository publishes a new transitive version — silently, with no line changing in any
    // diff, and against a census `THIRD_PARTY_LICENSES.md` records as a completed review. The lock files committed
    // beside each module are what fix that closure, exactly as `pnpm-lock.yaml` and `Cargo.lock` fix the other two;
    // `./gradlew :app:dependencies --write-locks` is what rewrites them, and it belongs to the change that moves a pin.
    dependencyLocking {
        lockAllConfigurations()
    }
}

tasks.register("clean").configure {
    delete("build")
}
