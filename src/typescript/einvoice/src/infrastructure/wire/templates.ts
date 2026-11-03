import type { TemplateDto } from '../../domain/dtos.js';

/** Incoming template row from `/webapp/templates` (MISA uses `IPTemplateID` here, `InvoiceTemplateID` on insert). */
export interface WireTemplate {
  IPTemplateID: string;
  InvSeries: string;
  TemplateName: string;
  InvTemplateNo?: string | null;
  TemplateType: number;
  Inactive: boolean;
  IsMoreVATRate: boolean;
}

/** Port of `InvoiceDtoMapper.FromWire(TemplateDto, defaultWithCode)`: series char 2 is C (with code) or K (without). */
export function fromWireTemplate(w: WireTemplate, defaultWithCode = false): TemplateDto {
  let withCode = defaultWithCode;
  if (w.InvSeries.length > 1) {
    const c = w.InvSeries.charAt(1).toUpperCase();
    if (c === 'C') withCode = true;
    else if (c === 'K') withCode = false;
  }
  return {
    ipTemplateID: w.IPTemplateID,
    invSeries: w.InvSeries,
    templateName: w.TemplateName,
    ...(w.InvTemplateNo != null ? { invTemplateNo: w.InvTemplateNo } : {}),
    templateType: w.TemplateType,
    usesTaxAuthorityCode: withCode,
    isActive: !w.Inactive,
    isMoreVATRate: w.IsMoreVATRate,
  };
}
