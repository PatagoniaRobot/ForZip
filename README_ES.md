# ForZip — Herramienta Forense de Empaquetado y Verificación

**ForZip** es una utilidad forense de código abierto diseñada para el empaquetado seguro de evidencia digital. A diferencia de los compresores convencionales, ForZip implementa un flujo de trabajo estrictamente forense que garantiza la integridad y trazabilidad de los datos desde el momento de su captura hasta su análisis posterior.

## Características Principales

*   **Procesamiento de Doble Fase**: Realiza el cálculo de hashes criptográficos de los archivos originales *antes* de iniciar la compresión, permitiendo documentar el estado exacto de la evidencia en su origen.
*   **Seguridad Avanzada**: Implementa cifrado de grado militar **AES-256** para proteger el contenido de los contenedores de evidencia.
*   **Soporte de Grandes Volúmenes**: Utiliza tecnología **Zip64** de forma nativa, permitiendo gestionar contenedores de evidencia que superan los 4GB, ideal para imágenes de disco y grandes bases de datos.
*   **Informes Forenses Automáticos**: Genera reportes detallados en formato `.report.txt` que incluyen metadatos del operador, detalles del sistema, parámetros de la operación y hashes individuales de cada archivo procesado.
*   **Verificación de Integridad**: Permite validar contenedores de evidencia existentes comparando los archivos actuales contra informes forenses generados previamente.
*   **Interfaz Moderna y Dinámica**: Diseñada con una estética oscura premium, optimizada para largos periodos de trabajo y con soporte completo para cambio de idioma (Español/Inglés) en tiempo real.

## Módulos del Software

1.  **Empaquetar (Compress)**: El núcleo del sistema. Permite agregar archivos y carpetas, seleccionar algoritmos de hash (MD5, SHA-1, SHA-256, SHA-512), definir el nivel de compresión y aplicar cifrado.
2.  **Extraer (Extract)**: Descompresión segura de archivos ZIP, con soporte para contenedores cifrados y registro detallado de eventos en la bitácora.
3.  **Hash Batch**: Herramienta de cálculo masivo de hashes para archivos en disco sin necesidad de empaquetarlos, ideal para inventarios rápidos de evidencia.
4.  **Verificar**: Motor de auditoría que carga reportes ForZip y verifica que la evidencia en disco no haya sido alterada.
5.  **Generador de Contraseñas**: Utilidad integrada para crear contraseñas de alta entropía adecuadas para la protección de evidencia sensible.

## Guía Rápida de Uso

1.  **Para empaquetar evidencia**:
    *   Arrastre los archivos a la zona de "Empaquetar".
    *   Seleccione los algoritmos de hash deseados.
    *   Defina la ruta del archivo de salida.
    *   Presione "Empaquetar". Se realizará primero el hasheado (0-50% del progreso) y luego la compresión (50-100%).
    *   Al finalizar, complete los datos del operador para generar el informe forense.

2.  **Para verificar integridad**:
    *   Vaya a la pestaña "Verificar".
    *   Cargue el archivo de informe `.report.txt`.
    *   El sistema buscará los archivos referenciados y validará sus hashes, emitiendo un veredicto de integridad.

## Requisitos del Sistema

*   **SO**: Windows 10/11 (x64)
*   **Runtime**: .NET 8.0 (incluido en la versión self-contained)

---
© 2026 Patagonia Robot — Tecnología Forense Avanzada  
Desarrollado por Claudio Andino  
Licencia: Apache License 2.0
