# RadioLogger 2026 - Consola de Testigos Profesional

**RadioLogger** es una aplicación de escritorio avanzada para Windows (WPF/C#) diseñada para el monitoreo y grabación 24/7 de múltiples estaciones de radio de forma simultánea. Utiliza el motor de audio **BASS** para una estabilidad de grado industrial.

## 🚀 Características Clave

*   **Grabación Multi-Canal:** Captura desde múltiples entradas de hardware (tarjetas de sonido) en paralelo.
*   **Streaming Nativo:** Cliente Shoutcast (v1/v2) e Icecast integrado con soporte de reconexión automática.
*   **Consola 3D Realista:** Interfaz visual con vúmetros logarítmicos, picos "Peak Hold" y faders de precisión.
*   **Gestión de Testigos:** Auditor (Player) integrado con navegación jerárquica por Emisora y Fecha.
*   **Monitoreo 24/7:** Detección automática de silencio, alertas de espacio en disco y rotación de archivos diaria.
*   **Arquitectura Robusta:** Compilación forzada a x64 para máximo rendimiento con drivers de audio modernos.

## 🛠️ Tecnologías

*   **.NET 10 (C# / WPF)**
*   **ManagedBass / ManagedBass.Enc** (Wrappers para BASS Audio Library)
*   **CommunityToolkit.Mvvm**
*   **Newtonsoft.Json**

## 🔧 Configuración para Desarrollo

1.  **Dependencias Nativas:** Es necesario descargar las DLLs de `bass.dll` y `bass_enc.dll` (64 bits) de [Un4seen Developments](http://www.un4seen.com/) y colocarlas en la carpeta de salida (`bin/x64/Debug/`).
2.  **Encoder:** Colocar `lame.exe` en la raíz de la aplicación para permitir la compilación a MP3.
3.  **Compilación:** Asegurarse de compilar el proyecto bajo la configuración de plataforma **x64**.

## 📊 Roadmap

- [x] Gestión de nombres de estación personalizados.
- [x] Navegación avanzada en el Auditor (Player).
- [ ] Integración con **SignalR** para monitoreo remoto en tiempo real.
- [ ] Soporte para ejecución en el **System Tray**.
- [ ] Auto-inicio con Windows.

---
© 2026 RadioLogger Project.
