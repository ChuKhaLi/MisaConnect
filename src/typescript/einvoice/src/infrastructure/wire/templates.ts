import type { TemplateDto, TicketTemplateDto } from '../../domain/dtos.js';

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

/** Incoming row from `/webapp/templates/ticket` (MISA API document §8.2 "Ticket"), field names verbatim. */
export interface WireTicketTemplate {
  TicketTemplateID: string;
  TemplateName?: string | null;
  InvSeries?: string | null;
  Inactive?: boolean | null;
  TicketType?: number | null;
  TemplateType?: number | null;
  ServiceName?: string | null;
  IsInheritFromOldTemplate?: boolean | null;
  IsTicketCode?: boolean | null;
}

/** Port of `InvoiceDtoMapper.FromWire(TicketTemplateDto)`: absent strings → '', ints → 0, bools → false. */
export function fromWireTicketTemplate(w: WireTicketTemplate): TicketTemplateDto {
  return {
    ticketTemplateID: w.TicketTemplateID,
    templateName: typeof w.TemplateName === 'string' ? w.TemplateName : '',
    invSeries: typeof w.InvSeries === 'string' ? w.InvSeries : '',
    isActive: w.Inactive !== true,
    ticketType: typeof w.TicketType === 'number' ? w.TicketType : 0,
    templateType: typeof w.TemplateType === 'number' ? w.TemplateType : 0,
    ...(typeof w.ServiceName === 'string' ? { serviceName: w.ServiceName } : {}),
    isInheritFromOldTemplate: w.IsInheritFromOldTemplate === true,
    isTicketCode: w.IsTicketCode === true,
  };
}
