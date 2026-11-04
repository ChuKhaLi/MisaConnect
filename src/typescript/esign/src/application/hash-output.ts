/** Parsed `documents/hash` entry per format. Plain JSON data (no binary): safe to persist. */
export interface PdfHashOutput {
  format: 'Pdf';
  documentId?: string | undefined;
  documentBytes?: string | undefined;
  documentHash?: string | undefined;
  sh?: string | undefined;
  signatureName?: string | undefined;
  digest: string;
}

export interface XmlHashOutput {
  format: 'Xml';
  documentId?: string | undefined;
  /** Raw XML text MISA returns for attachment. */
  document: string;
  signatureId: string;
  digest: string;
  sh: string;
}

export interface WordExcelHashOutput {
  format: 'Word' | 'Excel';
  documentId?: string | undefined;
  documentBytes: string;
  signatureId: string;
  digest: string;
  mainDom: string;
}

export type HashOutput = PdfHashOutput | XmlHashOutput | WordExcelHashOutput;
