import type { MailAttachment } from '../types/mail'

export function buildAttachmentUrl(accountId: string, messageId: string, attachment: MailAttachment, download = false) {
  const query = new URLSearchParams({
    messageId,
    attachmentId: attachment.id,
    fileName: attachment.name,
  })
  if (download) query.set('download', 'true')

  const isPdf = attachment.contentType.toLowerCase() === 'application/pdf' || /\.pdf$/i.test(attachment.name)
  const viewerFragment = !download && isPdf ? '#page=1&view=Fit&zoom=page-fit&navpanes=0&pagemode=none' : ''
  return `/api/mail/attachments/${encodeURIComponent(accountId)}?${query.toString()}${viewerFragment}`
}
