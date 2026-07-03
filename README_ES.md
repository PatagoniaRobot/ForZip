# ForZip — Herramienta Forense de Empaquetado y Verificación

**ForZip** es una utilidad forense de código abierto diseñada para el empaquetado seguro de evidencia digital. A diferencia de los compresores convencionales, ForZip implementa un flujo de trabajo estrictamente forense que garantiza la integridad y trazabilidad de los datos desde el momento de su captura hasta su análisis posterior.

## Características Principales

*   **Hashing en un Solo Pase**: Calcula los hashes criptográficos de los archivos originales *durante* la compresión (una sola lectura de disco), documentando el estado exacto de la evidencia en su origen sin duplicar el I/O.
*   **Seguridad Avanzada**: Implementa cifrado de grado militar **AES-256** para proteger el contenido de los contenedores de evidencia.
*   **Soporte de Grandes Volúmenes**: Utiliza tecnología **Zip64** de forma nativa, permitiendo gestionar contenedores de evidencia que superan los 4GB, ideal para imágenes de disco y grandes bases de datos.
*   **Informes y Manifiesto Forenses**: Genera un reporte legible `.report.txt` (metadatos del operador, sistema, parámetros y hashes individuales, con rutas de origen y marcas temporales) y un **manifiesto `.manifest.json`** legible por máquina, fuente de verdad para la verificación automática.
*   **Verificación de Evidencia**: Re-hashea el contenido del ZIP contra su manifiesto y emite un veredicto **archivo por archivo** (OK / alterado / faltante / añadido), además de verificar el hash global del contenedor.
*   **Firma Digital**: Firma opcional del manifiesto (CMS/PKCS#7) con el certificado X.509 del operador, haciendo evidente cualquier manipulación. Verificable de forma independiente (p. ej. con OpenSSL).
*   **Interfaz Moderna y Dinámica**: Diseñada con una estética oscura premium, optimizada para largos periodos de trabajo y con soporte completo para cambio de idioma (Español/Inglés) en tiempo real.

## Módulos del Software

1.  **Empaquetar (Compress)**: El núcleo del sistema. Permite agregar archivos y carpetas, seleccionar algoritmos de hash (MD5, SHA-1, SHA-256, SHA-512), definir el nivel de compresión y aplicar cifrado.
2.  **Extraer (Extract)**: Descompresión segura de archivos ZIP, con soporte para contenedores cifrados y registro detallado de eventos en la bitácora.
3.  **Hash Batch**: Herramienta de cálculo masivo de hashes para archivos en disco sin necesidad de empaquetarlos, ideal para inventarios rápidos de evidencia.
4.  **Verificar**: Motor de auditoría que verifica la integridad de un informe (contra su `.sha256`) o re-hashea un contenedor ZIP contra su manifiesto, validando además la firma digital cuando existe.
5.  **Generador de Contraseñas**: Utilidad integrada para crear contraseñas de alta entropía adecuadas para la protección de evidencia sensible.

## Guía Rápida de Uso

1.  **Para empaquetar evidencia**:
    *   Arrastre los archivos a la zona de "Empaquetar".
    *   Seleccione los algoritmos de hash deseados.
    *   Defina la ruta del archivo de salida.
    *   Presione "Empaquetar". El hasheado y la compresión ocurren en un solo pase (0-100%).
    *   Al finalizar, complete los datos del operador para generar el informe forense (opcionalmente, firme el manifiesto con su certificado).

2.  **Para verificar integridad**:
    *   Vaya a la pestaña "Verificar".
    *   Cargue el informe `.report.txt` (verifica su `.sha256`) o el `.manifest.json` (re-hashea la evidencia del ZIP).
    *   El sistema emite un veredicto archivo por archivo y, si el manifiesto está firmado, valida la firma digital.

## Requisitos del Sistema

*   **SO**: Windows 10/11 (x64)
*   **Runtime**: .NET 8.0 (incluido en la versión self-contained)

---
© 2026 Patagonia Robot — Tecnología Forense Avanzada  
Desarrollado por Claudio Andino  
Licencia: Apache License 2.0
