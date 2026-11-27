import { useEffect } from 'react';
import { useLocation, useParams, useSearchParams } from 'react-router-dom';
import { PositionContent } from '../position/PositionContent';
import { parseChartInterval } from '../position/positionFormatting';
import { usePositionEvaluationRequests } from '../position/usePositionEvaluationRequests';

export { PositionPreview } from '../position/PositionContent';

export function PositionPage() {
  const { id = '' } = useParams();
  const { state } = useLocation();
  const [searchParams, setSearchParams] = useSearchParams();
  const rawInterval = searchParams.get('interval');
  const interval = parseChartInterval(rawInterval);
  const requests = usePositionEvaluationRequests();

  useEffect(() => {
    if (rawInterval !== null && rawInterval !== interval) {
      setSearchParams((current) => {
        const next = new URLSearchParams(current);
        next.set('interval', interval);
        return next;
      }, { replace: true, state });
    }
  }, [rawInterval, interval, setSearchParams, state]);

  return (
    <PositionContent
      id={id}
      interval={interval}
      onIntervalChange={(nextInterval) => {
        if (nextInterval === interval) return;
        setSearchParams((current) => {
          const next = new URLSearchParams(current);
          next.set('interval', nextInterval);
          return next;
        }, { state });
      }}
      requests={requests}
      routeState={state}
    />
  );
}
